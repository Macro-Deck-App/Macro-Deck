using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using Serilog;

namespace MacroDeckHost.Application.Plugins.Capabilities;

public sealed class PluginCapabilityInvoker : IPluginCapabilityInvoker, IDisposable
{
	// Generous relative to MaxConcurrentInvocations * a handful of plugins - bounded so a peer that
	// never stops abandoning correlations cannot grow this without limit, and time-pruned so a
	// correlation that could not possibly still receive a meaningful late reply does not linger either.
	private const int MaxAbandonedEntries = 1024;

	private readonly IPluginSessionRegistry _sessionRegistry;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;

	private readonly ConcurrentDictionary<string, PendingInvocation> _pending = new(StringComparer.Ordinal);

	private readonly ConcurrentDictionary<string, DateTimeOffset> _abandoned = new(StringComparer.Ordinal);

	private readonly ConcurrentDictionary<string, SemaphoreSlim> _concurrencyByPlugin = new(StringComparer.Ordinal);

	public PluginCapabilityInvoker(IPluginSessionRegistry sessionRegistry,
		TimeProvider timeProvider,
		ILogger logger)
	{
		_sessionRegistry = sessionRegistry;
		_timeProvider = timeProvider;
		_logger = logger.ForContext<PluginCapabilityInvoker>();

		_sessionRegistry.SessionEnded += OnSessionEnded;
	}

	public async Task<JsonElement?> InvokeAsync(string pluginId,
		CapabilityInvokeRequest request,
		CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrEmpty(pluginId);
		ArgumentNullException.ThrowIfNull(request);

		// The ceiling is the protocol's invoke timeout even when the caller asked for longer: a deadline
		// the plugin will not wait for only keeps this side waiting for nothing.
		var budget = request.Timeout is { } requested && requested < ProtocolTimeouts.CapabilityInvoke
			? requested
			: ProtocolTimeouts.CapabilityInvoke;

		var correlationId = Guid.CreateVersion7().ToString();

		var envelope = new ProtocolEnvelope
		{
			Type = MessageTypes.CapabilityInvoke,
			Id = correlationId,
			DeadlineMs = (int)budget.TotalMilliseconds,
			IdempotencyKey = request.IdempotencyKey,
			Payload = JsonSerializer.SerializeToElement(new CapabilityInvokePayload
				{
					Kind = request.Kind,
					LocalId = request.LocalId,
					Operation = request.Operation,
					Arguments = SerializeArguments(request.Arguments)
				},
				PluginProtocolJson.Options)
		};

		if (ProtocolEnvelopeWriter.WriteToUtf8Bytes(envelope).Length > ProtocolLimits.MaxMessageBytes)
		{
			throw RemoteCapabilityException.CreateNonRetryable(ProtocolErrorCodes.PayloadTooLarge,
				ProtocolErrorMessages.For(ProtocolErrorCodes.PayloadTooLarge));
		}

		var semaphore = _concurrencyByPlugin.GetOrAdd(pluginId,
			static _ => new SemaphoreSlim(ProtocolLimits.MaxConcurrentInvocations));

		if (!await semaphore.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false))
		{
			throw RemoteCapabilityException.CreateRetryable(ProtocolErrorCodes.RateLimited,
				$"This plugin is already running {ProtocolLimits.MaxConcurrentInvocations} invocations.");
		}

		var completion = new TaskCompletionSource<ProtocolEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
		_pending[correlationId] = new PendingInvocation(pluginId, request.Kind, request.Operation, completion);

		try
		{
			// The send races the same budget as the wait, rather than running before it starts: a
			// plugin that stops draining its socket parks the underlying WebSocket send (see
			// PluginWebSocketConnection.Send), and without this race a parked send would keep this call
			// - and the concurrency slot it holds - alive forever for a caller that passed
			// CancellationToken.None, which the broadcast and polling background services both do.
			var sentAt = _timeProvider.GetUtcNow();
			var sendTask = _sessionRegistry.SendToPlugin(pluginId, envelope, cancellationToken);
			var sendTimeoutTask = Task.Delay(budget, _timeProvider, CancellationToken.None);

			if (ReferenceEquals(await Task.WhenAny(sendTask, sendTimeoutTask).ConfigureAwait(false),
				sendTimeoutTask))
			{
				Abandon(correlationId);
				ObserveAbandonedSend(sendTask, pluginId, correlationId);
				throw RemoteCapabilityException.CreateRetryable(ProtocolErrorCodes.Timeout,
					ProtocolErrorMessages.For(ProtocolErrorCodes.Timeout));
			}

			var delivered = await sendTask.ConfigureAwait(false);

			if (!delivered)
			{
				throw RemoteCapabilityException.CreateRetryable(ProtocolErrorCodes.CapabilityUnavailable,
					"The plugin has no attached connection.");
			}

			// What is left of the budget after the send, so the two phases together cannot outlive it -
			// stacking a fresh full budget onto each would let a slow send double the invoke's own timeout.
			var remaining = budget - (_timeProvider.GetUtcNow() - sentAt);
			if (remaining < TimeSpan.Zero)
			{
				remaining = TimeSpan.Zero;
			}

			return await AwaitResultAsync(pluginId, correlationId, completion, remaining, cancellationToken)
				.ConfigureAwait(false);
		}
		finally
		{
			_pending.TryRemove(correlationId, out _);
			semaphore.Release();
		}
	}

	public bool TryComplete(string pluginId, ProtocolEnvelope result)
	{
		var correlationId = result.CorrelationId;
		if (string.IsNullOrEmpty(correlationId))
		{
			return false;
		}

		if (_pending.TryGetValue(correlationId, out var pending))
		{
			if (!string.Equals(pending.PluginId, pluginId, StringComparison.Ordinal))
			{
				return false;
			}

			_pending.TryRemove(correlationId, out _);
			pending.Completion.TrySetResult(result);
			return true;
		}

		if (_abandoned.ContainsKey(correlationId))
		{
			PluginCapabilityLog.LateResultDropped(_logger, pluginId, correlationId);
			return true;
		}

		return false;
	}

	public void AbortAll(string pluginId, ProtocolError reason)
	{
		foreach (var (correlationId, pending) in _pending)
		{
			if (!string.Equals(pending.PluginId, pluginId, StringComparison.Ordinal))
			{
				continue;
			}

			if (_pending.TryRemove(correlationId, out _))
			{
				pending.Completion.TrySetException(RemoteCapabilityException.From(reason));
			}
		}
	}

	public bool IsLiveActionExecute(string pluginId, string correlationId)
		=> _pending.TryGetValue(correlationId, out var pending) &&
			string.Equals(pending.PluginId, pluginId, StringComparison.Ordinal) &&
			string.Equals(pending.Kind, CapabilityKinds.Actions, StringComparison.Ordinal) &&
			string.Equals(pending.Operation, CapabilityOperations.Actions.Execute, StringComparison.Ordinal);

	public void Dispose()
	{
		_sessionRegistry.SessionEnded -= OnSessionEnded;

		foreach (var semaphore in _concurrencyByPlugin.Values)
		{
			semaphore.Dispose();
		}
	}

	private void OnSessionEnded(object? sender, PluginSessionEndedEventArgs e)
		=> AbortAll(e.PluginId, SessionEndedError(e.Reason));

	private static ProtocolError SessionEndedError(PluginSessionEndReason reason)
		=> new()
		{
			Code = ProtocolErrorCodes.CapabilityUnavailable,
			Message = reason == PluginSessionEndReason.Detached
				? "The plugin's connection dropped before it replied."
				: "The plugin's session ended before it replied.",
			Retryable = true
		};

	private async Task<JsonElement?> AwaitResultAsync(
		string pluginId,
		string correlationId,
		TaskCompletionSource<ProtocolEnvelope> completion,
		TimeSpan budget,
		CancellationToken cancellationToken)
	{
		var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var registration = cancellationToken.Register(
			static state => ((TaskCompletionSource)state!).TrySetResult(),
			cancelled);

		var timeoutTask = Task.Delay(budget, _timeProvider, CancellationToken.None);
		var winner = await Task.WhenAny(completion.Task, timeoutTask, cancelled.Task).ConfigureAwait(false);

		if (ReferenceEquals(winner, cancelled.Task))
		{
			// The caller's token fired: move on at once rather than wait out the plugin's CANCELLED reply
			// - see SliderActionService's remarks on why holding a slot open for a full budget re-creates
			// issue #189's wedge. The cancel is best-effort; the correlation moves to the abandoned set so
			// a reply that crosses it on the wire is dropped, not reported.
			Abandon(correlationId);
			await SendCancelBestEffortAsync(pluginId, correlationId).ConfigureAwait(false);
			throw new OperationCanceledException(cancellationToken);
		}

		if (ReferenceEquals(winner, timeoutTask))
		{
			Abandon(correlationId);
			throw RemoteCapabilityException.CreateRetryable(ProtocolErrorCodes.Timeout,
				ProtocolErrorMessages.For(ProtocolErrorCodes.Timeout));
		}

		var resultEnvelope = await completion.Task.ConfigureAwait(false);
		return ToResult(resultEnvelope);
	}

	private void Abandon(string correlationId)
	{
		_pending.TryRemove(correlationId, out _);
		_abandoned[correlationId] = _timeProvider.GetUtcNow();
		PruneAbandoned();
	}

	private void PruneAbandoned()
	{
		var cutoff = _timeProvider.GetUtcNow() - ProtocolTimeouts.CapabilityInvoke;

		foreach (var (correlationId, abandonedAt) in _abandoned)
		{
			if (abandonedAt < cutoff)
			{
				_abandoned.TryRemove(correlationId, out _);
			}
		}

		var overflow = _abandoned.Count - MaxAbandonedEntries;
		if (overflow <= 0)
		{
			return;
		}

		foreach (var correlationId in _abandoned.OrderBy(pair => pair.Value)
			.Select(pair => pair.Key)
			.Take(overflow))
		{
			_abandoned.TryRemove(correlationId, out _);
		}
	}

	private void ObserveAbandonedSend(Task<bool> sendTask, string pluginId, string correlationId)
		=> _ = sendTask.ContinueWith(task =>
			{
				if (task.IsFaulted)
				{
					PluginCapabilityLog.SendAbandoned(_logger, pluginId, correlationId, task.Exception!);
				}
			},
			CancellationToken.None,
			TaskContinuationOptions.ExecuteSynchronously,
			TaskScheduler.Default);

	private async Task SendCancelBestEffortAsync(string pluginId, string correlationId)
	{
		try
		{
			await _sessionRegistry.SendToPlugin(pluginId,
					new ProtocolEnvelope
					{
						Type = MessageTypes.CapabilityCancel,
						Id = Guid.CreateVersion7().ToString(),
						CorrelationId = correlationId,
						Payload = JsonSerializer.SerializeToElement(
							new CapabilityCancelPayload { Reason = "The caller cancelled." },
							PluginProtocolJson.Options)
					},
					CancellationToken.None)
				.ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			PluginCapabilityLog.CancelDeliveryFailed(_logger, pluginId, correlationId, exception);
		}
	}

	private static JsonElement? SerializeArguments(object? arguments)
		=> arguments switch
		{
			null => null,
			JsonElement element => element,
			_ => JsonSerializer.SerializeToElement(arguments, PluginProtocolJson.Options)
		};

	private static JsonElement? ToResult(ProtocolEnvelope envelope)
	{
		if (envelope.Error is { } error)
		{
			throw RemoteCapabilityException.From(error);
		}

		return envelope.Payload?.Deserialize<CapabilityResultPayload>(PluginProtocolJson.Options)?.Data;
	}

	private sealed record PendingInvocation(
		string PluginId,
		string Kind,
		string Operation,
		TaskCompletionSource<ProtocolEnvelope> Completion);
}
