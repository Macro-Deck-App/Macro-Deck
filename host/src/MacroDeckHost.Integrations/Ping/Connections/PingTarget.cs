using MacroDeck.Sdk.Logging;
using MacroDeckHost.Integrations.Ping.Configuration;
using MacroDeckHost.Integrations.Ping.Protocol;
using MacroDeckHost.Integrations.Ping.State;
using Serilog;

namespace MacroDeckHost.Integrations.Ping.Connections;

internal delegate void PingPublisher(PingTarget source, PingSnapshot snapshot, PingReachability? transition);

internal sealed class PingTarget : IAsyncDisposable
{
	public const int LossWindow = 20;

	private static readonly ILogger _logger = IntegrationLog.For<PingTarget>(PingIntegration.IntegrationId);

	private readonly IPingProber _prober;
	private readonly TimeProvider _time;
	private readonly PingPublisher _publish;
	private readonly CancellationTokenSource _stop = new();
	private readonly SemaphoreSlim _pollGate = new(1, 1);
	private readonly Lock _sync = new();
	private readonly Queue<bool> _outcomes = new();

	private PingSnapshot _snapshot;
	private PingTargetSettings _settings;
	private CancellationTokenSource? _waiting;
	private int _consecutiveFailures;
	private Task? _loop;

	public PingTarget(
		Guid entryId,
		string title,
		string key,
		PingTargetSettings settings,
		IPingProber prober,
		TimeProvider time,
		PingPublisher publish)
	{
		_settings = settings;
		_prober = prober;
		_time = time;
		_publish = publish;
		_snapshot = new PingSnapshot(entryId, title, key, settings.Host) { Interval = settings.Interval };
	}

	public PingSnapshot Snapshot
	{
		get
		{
			lock (_sync)
			{
				return _snapshot;
			}
		}
	}

	public PingTargetSettings Settings
	{
		get
		{
			lock (_sync)
			{
				return _settings;
			}
		}
	}

	public void Start()
	{
		_loop ??= Task.Run(RunAsync);
	}

	public void Retune(string title, string key, PingTargetSettings settings)
	{
		PingSnapshot next;
		lock (_sync)
		{
			_settings = settings;
			next = _snapshot = _snapshot with { Title = title, Key = key, Interval = settings.Interval };
			_waiting?.Cancel();
		}

		_publish(this, next, null);
	}

	public async Task PollAsync(CancellationToken cancellationToken)
	{
		using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
		await _pollGate.WaitAsync(stopping.Token).ConfigureAwait(false);
		try
		{
			var settings = Settings;
			var result = await _prober.ProbeAsync(settings.Host, settings.Timeout, stopping.Token)
				.ConfigureAwait(false);

			PingSnapshot previous;
			PingSnapshot next;
			PingReachability? transition;
			lock (_sync)
			{
				previous = _snapshot;
				(next, transition) = Apply(previous, result, _settings.FailureThreshold);
				_snapshot = next;
			}

			if (next.Problem != previous.Problem && next.Problem != PingProblem.None)
			{
				_logger.Information("Ping {Title} ({Host}) failed: {Problem}", next.Title, next.Host, next.Problem);
			}

			_publish(this, next, transition);
		}
		finally
		{
			_pollGate.Release();
		}
	}

	public async ValueTask DisposeAsync()
	{
		await _stop.CancelAsync().ConfigureAwait(false);

		if (_loop is not null)
		{
			try
			{
				await _loop.ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
			}
		}

		_stop.Dispose();
		_pollGate.Dispose();
	}

	private (PingSnapshot Next, PingReachability? Transition) Apply(
		PingSnapshot current,
		PingProbeResult result,
		int threshold)
	{
		if (result.Problem == PingProblem.NotPermitted)
		{
			return (current with { Problem = PingProblem.NotPermitted }, null);
		}

		Record(_outcomes, result.Succeeded, LossWindow);
		var loss = Math.Round(100d * _outcomes.Count(succeeded => !succeeded) / _outcomes.Count, 1);

		if (result.Succeeded)
		{
			_consecutiveFailures = 0;
			var online = current with
			{
				Reachability = PingReachability.Online,
				LatencyMilliseconds = result.RoundtripMilliseconds,
				PacketLossPercent = loss,
				LastSeen = _time.GetUtcNow(),
				Problem = PingProblem.None
			};
			return (online, current.Reachability == PingReachability.Offline ? PingReachability.Online : null);
		}

		_consecutiveFailures++;
		var failed = current with { PacketLossPercent = loss, Problem = result.Problem };
		if (_consecutiveFailures < threshold || current.Reachability == PingReachability.Offline)
		{
			return (failed, null);
		}

		var offline = failed with { Reachability = PingReachability.Offline, LatencyMilliseconds = null };
		return (offline, current.Reachability == PingReachability.Online ? PingReachability.Offline : null);
	}

	private static void Record<T>(Queue<T> queue, T value, int capacity)
	{
		queue.Enqueue(value);
		while (queue.Count > capacity)
		{
			queue.Dequeue();
		}
	}

	private async Task RunAsync()
	{
		var token = _stop.Token;

		while (!token.IsCancellationRequested)
		{
			try
			{
				await PollAsync(token).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested)
			{
				return;
			}
#pragma warning disable CA1031 // The poll loop must survive any single failed poll.
			catch (Exception exception)
#pragma warning restore CA1031
			{
				_logger.Error(exception, "Pinging {Title} failed", Snapshot.Title);
			}

			if (!await WaitAsync(token).ConfigureAwait(false))
			{
				return;
			}
		}
	}

	private async Task<bool> WaitAsync(CancellationToken token)
	{
		using var waiting = CancellationTokenSource.CreateLinkedTokenSource(token);
		TimeSpan interval;
		lock (_sync)
		{
			_waiting = waiting;
			interval = _settings.Interval;
		}

		try
		{
			await Task.Delay(interval, _time, waiting.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (!token.IsCancellationRequested)
		{
		}
		catch (OperationCanceledException)
		{
			return false;
		}
		finally
		{
			lock (_sync)
			{
				_waiting = null;
			}
		}

		return true;
	}
}
