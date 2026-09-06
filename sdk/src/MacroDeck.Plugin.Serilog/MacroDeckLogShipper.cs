using System.Text.Json;
using System.Threading.Channels;
using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Hosting.Credentials;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Logging;
using MacroDeck.Plugin.Protocol.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog.Debugging;

namespace MacroDeck.Plugin.Serilog;

/// <summary>
/// Drains <see cref="MacroDeckLogSink" /> and ships batches to the host over <c>log.publish</c>.
///
/// <para>
/// Every diagnostic this type emits about its own failures goes through <see cref="SelfLog" />, never
/// through an injected <c>ILogger</c> - an ordinary <c>_logger.Warning(...)</c> call would route
/// back through the very Serilog pipeline <see cref="UseMacroDeckLogging" /> installed, landing back on
/// <see cref="MacroDeckLogSink" /> as an ordinary event. That is not the reentrancy the sink's own
/// <c>[ThreadStatic]</c> guard defends against (this runs on an unrelated background loop, not nested
/// inside <c>Emit</c>), but it is exactly the kind of self-inflicted traffic a logging pipeline must
/// never generate about itself.
/// </para>
///
/// <para>
/// <c>log.publish</c> has no acknowledgement and there is no batch-level retry: a send that fails is
/// counted dropped and tee'd to the fallback file, matching <c>RemoteEventPublisher</c>'s documented
/// fire-and-forget contract and ADR 0026's at-most-once delivery with no replay log. What is still
/// queued when the connection returns gets drained normally; nothing already attempted is resent.
/// </para>
/// </summary>
internal sealed class MacroDeckLogShipper : IHostedService, IDisposable
{
	private static readonly TimeSpan _maxBackoff = TimeSpan.FromSeconds(30);
	private static readonly TimeSpan _minTick = TimeSpan.FromMilliseconds(50);

	/// <summary>Caps the final flush in <see cref="StopAsync" />. <c>SendAsync</c> writes to a bounded
	/// outbound channel with <c>FullMode = Wait</c>, and hosted services stop in reverse registration
	/// order - if the connection's own drain loop already stopped, a full queue would otherwise block
	/// this write forever. Short enough to not meaningfully delay shutdown, long enough for one batch to
	/// actually go out when the connection is still alive.</summary>
	private static readonly TimeSpan _finalFlushBudget = TimeSpan.FromSeconds(2);

	private readonly MacroDeckLogSink _sink;
	private readonly IMacroDeckLogTransport _transport;
	private readonly MacroDeckLoggingOptions _options;
	private readonly TimeProvider _timeProvider;
	private readonly MacroDeckLogFallbackFile? _fallback;

	private CancellationTokenSource? _stopping;
	private Task _loop = Task.CompletedTask;
	private bool _wasDisconnected = true;

	/// <summary>Dropped counts that a failed send never got to report. Folded into the next drain's
	/// count rather than lost, so <see cref="LogPublishPayload.Dropped" /> still reflects reality once a
	/// batch finally gets through. Only ever touched from the single drain loop, plus the one final flush
	/// in <see cref="StopAsync" /> which by then runs after that loop has already stopped.</summary>
	private int _pendingDropped;

	public MacroDeckLogShipper(
		MacroDeckLogSink sink,
		IMacroDeckLogTransport transport,
		IOptions<MacroDeckLoggingOptions> options,
		IOptions<PluginHostOptions> hostOptions,
		PluginMetadata metadata,
		TimeProvider timeProvider)
	{
		ArgumentNullException.ThrowIfNull(sink);
		ArgumentNullException.ThrowIfNull(transport);
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(hostOptions);
		ArgumentNullException.ThrowIfNull(metadata);
		ArgumentNullException.ThrowIfNull(timeProvider);

		_sink = sink;
		_transport = transport;
		_options = options.Value;
		_timeProvider = timeProvider;

		_fallback = _options.EnableFallbackFile
			? new MacroDeckLogFallbackFile(ResolveFallbackPath(hostOptions.Value, metadata),
				_options.FallbackFileMaxBytes)
			: null;
	}

	/// <summary>Never awaited by the caller - a plugin's startup must not wait on the host being
	/// reachable, and events logged before the first connection are exactly the ones most worth seeing
	/// (registration and handshake failures happen right there).</summary>
	public Task StartAsync(CancellationToken cancellationToken)
	{
		_stopping = new CancellationTokenSource();
		_loop = Task.Run(() => RunAsync(_stopping.Token), CancellationToken.None);
		return Task.CompletedTask;
	}

	public async Task StopAsync(CancellationToken cancellationToken)
	{
		var stopping = _stopping;

		if (stopping is not null)
		{
			await stopping.CancelAsync();

			try
			{
				await _loop.WaitAsync(cancellationToken);
			}
			catch (Exception exception) when (exception is OperationCanceledException or TimeoutException)
			{
				// Shutdown has a budget; the loop is already cancelled and waiting past it only delays
				// process exit.
			}
		}

		// Best-effort: whatever is still queued when the process stops is worth one last attempt rather
		// than being silently discarded - but bounded, not unbounded: a full outbound channel with no
		// drain loop left behind it must never hang shutdown.
		using var flushCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		flushCts.CancelAfter(_finalFlushBudget);

		try
		{
			await FlushOnceAsync(flushCts.Token);
		}
		catch (OperationCanceledException)
		{
			SelfLog.WriteLine("MacroDeckLogShipper: final flush on shutdown did not complete within its budget.");
		}
	}

	public void Dispose() => _stopping?.Dispose();

	private async Task RunAsync(CancellationToken cancellationToken)
	{
		var backoff = TimeSpan.Zero;
		var interval = EffectiveTickInterval();

		while (!cancellationToken.IsCancellationRequested)
		{
			await WaitForTickOrSignalAsync(interval, _sink.FlushRequested, cancellationToken);

			if (cancellationToken.IsCancellationRequested)
			{
				return;
			}

			if (!_transport.IsConnected)
			{
				// No busy-spin and no draining while disconnected: the queues fill and drop by their
				// own policy, and there is nothing to send through yet.
				_wasDisconnected = true;
				continue;
			}

			if (backoff > TimeSpan.Zero)
			{
				try
				{
					await Task.Delay(backoff, _timeProvider, cancellationToken);
				}
				catch (OperationCanceledException)
				{
					return;
				}
			}

			var succeeded = await FlushOnceAsync(cancellationToken);
			backoff = succeeded ? TimeSpan.Zero : NextBackoff(backoff);
		}
	}

	private TimeSpan EffectiveTickInterval()
	{
		var interval = _options.FlushInterval;
		return interval < _minTick ? _minTick : interval;
	}

	/// <summary>
	/// Wakes on whichever comes first: the flush interval, or the sink's own heuristic signal that a
	/// batch's worth of events has queued up. A fresh <see cref="Task.Delay(TimeSpan,TimeProvider,CancellationToken)" />
	/// each call rather than a shared <see cref="PeriodicTimer" />: a <see cref="PeriodicTimer" />
	/// allows only one outstanding <c>WaitForNextTickAsync</c> call at a time, and racing it against the
	/// signal here means whichever of the two loses the race is often still pending when this method is
	/// called again next iteration.
	/// </summary>
	private async Task WaitForTickOrSignalAsync(
		TimeSpan interval,
		ChannelReader<byte> flushRequested,
		CancellationToken cancellationToken)
	{
		var tick = Task.Delay(interval, _timeProvider, cancellationToken);
		var signal = flushRequested.ReadAsync(cancellationToken).AsTask();

		await Task.WhenAny(tick, signal).ConfigureAwait(false);

		// Whichever of the two did not win the race is left running; observed here so its eventual
		// (benign) cancellation or completion never surfaces as an unobserved task exception.
		ObserveQuietly(tick);
		ObserveQuietly(signal);
	}

	private static void ObserveQuietly(Task task)
		=> _ = task.ContinueWith(static t => _ = t.Exception,
			CancellationToken.None,
			TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
			TaskScheduler.Default);

	/// <summary>Drains whatever is queued and ships it, splitting as needed. Returns false if any part
	/// of what was drained failed to send, which drives the caller's backoff.</summary>
	private async Task<bool> FlushOnceAsync(CancellationToken cancellationToken)
	{
		if (!_transport.IsConnected)
		{
			return true;
		}

		var cap = Math.Min(_options.BatchSize, ProtocolLimits.MaxLogEventsPerBatch);
		var drained = _sink.Drain(cap);

		if (drained.Events.Count == 0 && drained.Dropped == 0 && _pendingDropped == 0)
		{
			return true;
		}

		var dtos = drained.Events.Select(LogEventProjection.ToDto).ToList();

		// Whatever a previous failed send never managed to report joins this drain's own count, so a
		// send failure never quietly loses drops - see SendBatchAsync's remarks on how the count returns
		// here when a send does not go through.
		var dropped = drained.Dropped + _pendingDropped;
		_pendingDropped = 0;

		var ok = await SendBatchAsync(dtos, dropped, cancellationToken);

		if (ok)
		{
			if (_wasDisconnected)
			{
				_fallback?.Reset();
				_wasDisconnected = false;
			}
		}
		else
		{
			_fallback?.Append(dtos);
		}

		return ok;
	}

	/// <summary>
	/// Sends one envelope, splitting the batch in half and sending the halves when the serialized
	/// envelope exceeds <see cref="ProtocolLimits.MaxMessageBytes" /> - the batch is shrunk to fit,
	/// never dropped for being oversize. <paramref name="dropped" /> is only ever attached to the first
	/// half of a split, so it is reported exactly once - and if that particular send fails, it is added
	/// back to <see cref="_pendingDropped" /> so the next successful batch still reports it instead of
	/// silently losing it.
	/// </summary>
	private async Task<bool> SendBatchAsync(List<LogEventDto> events, int dropped, CancellationToken cancellationToken)
	{
		if (events.Count == 0)
		{
			if (dropped == 0)
			{
				return true;
			}

			var sent = await SendEnvelopeAsync(BuildEnvelope(events, dropped), cancellationToken);
			if (!sent)
			{
				_pendingDropped += dropped;
			}

			return sent;
		}

		var envelope = BuildEnvelope(events, dropped);
		var bytes = ProtocolEnvelopeWriter.WriteToUtf8Bytes(envelope);

		if (bytes.Length <= ProtocolLimits.MaxMessageBytes || events.Count == 1)
		{
			var sent = await SendEnvelopeAsync(envelope, cancellationToken);
			if (!sent)
			{
				_pendingDropped += dropped;
			}

			return sent;
		}

		var mid = events.Count / 2;
		var first = events.GetRange(0, mid);
		var second = events.GetRange(mid, events.Count - mid);

		var firstOk = await SendBatchAsync(first, dropped, cancellationToken);
		var secondOk = await SendBatchAsync(second, 0, cancellationToken);

		return firstOk && secondOk;
	}

	private async Task<bool> SendEnvelopeAsync(ProtocolEnvelope envelope, CancellationToken cancellationToken)
	{
		try
		{
			await _transport.SendAsync(envelope, cancellationToken).ConfigureAwait(false);
			return true;
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			SelfLog.WriteLine("MacroDeckLogShipper: failed to send a log.publish batch: {0}", exception);
			return false;
		}
	}

	private static ProtocolEnvelope BuildEnvelope(IReadOnlyList<LogEventDto> events, int dropped)
		=> new()
		{
			Type = MessageTypes.LogPublish,
			Id = Guid.CreateVersion7().ToString(),
			Payload = JsonSerializer.SerializeToElement(
				new LogPublishPayload { Events = events, Dropped = dropped > 0 ? dropped : null },
				PluginProtocolJson.Options)
		};

	private static TimeSpan NextBackoff(TimeSpan current)
	{
		if (current <= TimeSpan.Zero)
		{
			return TimeSpan.FromSeconds(1);
		}

		var next = current + current;
		return next > _maxBackoff ? _maxBackoff : next;
	}

	/// <summary>Same resolution <see cref="FilePluginCredentialStore" /> uses for its own per-plugin
	/// directory: the configured state directory when set, otherwise the per-platform default.</summary>
	private static string ResolveFallbackPath(PluginHostOptions hostOptions, PluginMetadata metadata)
	{
		var root = string.IsNullOrEmpty(hostOptions.StateDirectory)
			? PluginStateDirectory.Default()
			: hostOptions.StateDirectory;

		return Path.Combine(root, metadata.Id, "logs", "plugin-fallback.log");
	}
}
