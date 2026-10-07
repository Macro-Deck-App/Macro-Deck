using MacroDeck.Sdk.Logging;
using MacroDeckHost.Application.AdGuardHome;
using Serilog;

namespace MacroDeckHost.Integrations.AdGuardHome;

internal sealed class AdGuardHomeInstance : IAsyncDisposable
{
	public static readonly TimeSpan StatusInterval = TimeSpan.FromSeconds(5);

	public static readonly TimeSpan StatisticsInterval = TimeSpan.FromSeconds(30);

	public static readonly TimeSpan FailureInterval = TimeSpan.FromSeconds(30);

	private static readonly ILogger _logger = IntegrationLog.For<AdGuardHomeInstance>(AdGuardHomeIntegration.IntegrationId);

	private readonly IAdGuardHomeClient _client;
	private readonly TimeProvider _time;
	private readonly Action<AdGuardHomeSnapshot> _publish;
	private readonly CancellationTokenSource _stop = new();
	private readonly SemaphoreSlim _pollGate = new(1, 1);
	private readonly SemaphoreSlim _wake = new(0, 1);
	private readonly Lock _sync = new();

	private AdGuardHomeSnapshot _snapshot;
	private DateTimeOffset? _statisticsFetchedAt;
	private Task? _loop;

	public AdGuardHomeInstance(
		string entryId,
		string title,
		string variableKey,
		AdGuardHomeConnectionSettings? settings,
		IAdGuardHomeClient client,
		TimeProvider time,
		Action<AdGuardHomeSnapshot> publish)
	{
		Settings = settings;
		_client = client;
		_time = time;
		_publish = publish;
		_snapshot = new AdGuardHomeSnapshot(entryId, title, variableKey, AdGuardHomeConnection.Connecting);
	}

	public AdGuardHomeSnapshot Snapshot
	{
		get
		{
			lock (_sync)
			{
				return _snapshot;
			}
		}
	}

	public IAdGuardHomeClient Client => _client;

	public AdGuardHomeConnectionSettings? Settings { get; }

	public void Start() => _loop ??= Task.Run(RunAsync);

	public async Task PollAsync(bool includeStatistics, CancellationToken cancellationToken)
	{
		using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
		await _pollGate.WaitAsync(stopping.Token);
		try
		{
			var current = Snapshot;
			AdGuardHomeSnapshot next;
			try
			{
				next = await FetchAsync(current, includeStatistics, cancellationToken);
			}
			catch (AdGuardHomeException exception) when (current.IsConnected &&
				exception.Failure == AdGuardHomeConnection.Unreachable)
			{
				next = await RetryAsync(current, includeStatistics, cancellationToken);
			}
			catch (AdGuardHomeException exception)
			{
				next = Failed(current, exception);
			}

			lock (_sync)
			{
				_snapshot = next;
			}

			_publish(next);
		}
		finally
		{
			_pollGate.Release();
		}
	}

	public void RequestPoll()
	{
		if (_wake.CurrentCount == 0)
		{
			try
			{
				_wake.Release();
			}
			catch (SemaphoreFullException)
			{
			}
		}
	}

	public async ValueTask DisposeAsync()
	{
		await _stop.CancelAsync();

		if (_loop is not null)
		{
			try
			{
				await _loop;
			}
			catch (OperationCanceledException)
			{
			}
		}

		_stop.Dispose();
		_pollGate.Dispose();
		_wake.Dispose();
	}

	private async Task<AdGuardHomeSnapshot> RetryAsync(
		AdGuardHomeSnapshot current,
		bool includeStatistics,
		CancellationToken cancellationToken)
	{
		try
		{
			return await FetchAsync(current, includeStatistics, cancellationToken);
		}
		catch (AdGuardHomeException exception)
		{
			return Failed(current, exception);
		}
	}

	private async Task<AdGuardHomeSnapshot> FetchAsync(
		AdGuardHomeSnapshot current,
		bool includeStatistics,
		CancellationToken cancellationToken)
	{
		var status = await _client.GetStatusAsync(cancellationToken);
		var now = _time.GetUtcNow();
		var next = current with
		{
			Connection = AdGuardHomeConnection.Connected,
			ProtectionEnabled = status.ProtectionEnabled,
			DnsRunning = status.Running,
			Version = status.Version,
			DisabledUntil = status.ProtectionDisabledFor is { } pause ? now + pause : null,
		};

		if (includeStatistics || current.Statistics is null || _statisticsFetchedAt is null ||
			now - _statisticsFetchedAt >= StatisticsInterval)
		{
			var stats = await _client.GetStatisticsAsync(cancellationToken);
			_statisticsFetchedAt = now;
			next = next with
			{
				Statistics = new AdGuardHomeStatistics(stats.DnsQueries,
					stats.BlockedFiltering,
					stats.SafeBrowsing,
					stats.SafeSearch,
					stats.Parental,
					stats.AverageProcessingSeconds * 1000)
			};
		}

		return next;
	}

	private AdGuardHomeSnapshot Failed(AdGuardHomeSnapshot current, AdGuardHomeException exception)
	{
		if (current.Connection != exception.Failure)
		{
			_logger.Warning("AdGuard Home {Title} is {Failure}: {Message}",
				current.Title,
				exception.Failure,
				exception.Message);
		}

		_statisticsFetchedAt = null;
		return new AdGuardHomeSnapshot(current.EntryId, current.Title, current.VariableKey, exception.Failure);
	}

	private async Task RunAsync()
	{
		var token = _stop.Token;

		while (!token.IsCancellationRequested)
		{
			try
			{
				await PollAsync(includeStatistics: false, token);
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested)
			{
				return;
			}
#pragma warning disable CA1031 // The poll loop must survive any single failed poll.
			catch (Exception exception)
#pragma warning restore CA1031
			{
				_logger.Error(exception, "Polling AdGuard Home {Title} failed", Snapshot.Title);
			}

			var delay = Snapshot.IsConnected ? StatusInterval : FailureInterval;
			try
			{
				await _wake.WaitAsync(delay, token);
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested)
			{
				return;
			}
		}
	}
}
