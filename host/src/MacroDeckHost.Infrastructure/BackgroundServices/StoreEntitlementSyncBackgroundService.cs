using System.Threading.Channels;
using MacroDeckHost.Application.Connect;
using MacroDeckHost.Application.Store;
using MacroDeckHost.Application.Store.Operations;
using MacroDeckHost.Application.Store.Reviews;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class StoreEntitlementSyncBackgroundService : BackgroundService
{
	internal static readonly TimeSpan CatalogWaitInterval = TimeSpan.FromSeconds(30);

	internal static readonly IReadOnlyList<TimeSpan> RetryDelays =
		[TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30)];

	private readonly IConnectSessionService _session;
	private readonly IStoreOperationTracker _operations;
	private readonly IStoreRegistryRefreshTracker _refreshes;
	private readonly IStoreOfficialPackages _packages;
	private readonly IStorePlatformClient _platform;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;
	private readonly Channel<Signal> _signals = Channel.CreateUnbounded<Signal>();

	private bool _signedIn;
	private ConnectConnectivity _connectivity = ConnectConnectivity.Ok;
	private bool _pendingFullClaim;
	private readonly HashSet<string> _pendingIds = new(StringComparer.OrdinalIgnoreCase);
	private int _failedAttempts;
	private DateTimeOffset? _dueAt;

	public StoreEntitlementSyncBackgroundService(IConnectSessionService session,
		IStoreOperationTracker operations,
		IStoreRegistryRefreshTracker refreshes,
		IStoreOfficialPackages packages,
		IStorePlatformClient platform,
		TimeProvider timeProvider,
		ILogger logger)
	{
		_session = session;
		_operations = operations;
		_refreshes = refreshes;
		_packages = packages;
		_platform = platform;
		_timeProvider = timeProvider;
		_logger = logger.ForContext<StoreEntitlementSyncBackgroundService>();
	}

	// Subscribing before reading Current means a SignedIn published in between is seen at least once;
	// a duplicate is harmless because only a transition arms a claim.
	public override Task StartAsync(CancellationToken cancellationToken)
	{
		_session.SessionChanged += OnSessionChanged;
		_operations.Changed += OnOperationChanged;
		_refreshes.Changed += OnRefreshChanged;
		_signals.Writer.TryWrite(new SessionSignal(_session.Current));

		return base.StartAsync(cancellationToken);
	}

	public override async Task StopAsync(CancellationToken cancellationToken)
	{
		_session.SessionChanged -= OnSessionChanged;
		_operations.Changed -= OnOperationChanged;
		_refreshes.Changed -= OnRefreshChanged;
		await base.StopAsync(cancellationToken);
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				await WaitForWork(stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				return;
			}

			while (_signals.Reader.TryRead(out var signal))
			{
				Apply(signal);
			}

			if (_dueAt is { } dueAt && dueAt <= _timeProvider.GetUtcNow())
			{
				_dueAt = null;
				try
				{
					await Claim(stoppingToken);
				}
				catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
				{
					return;
				}
				catch (Exception ex)
				{
					_logger.Warning(ex, "Claiming installed Store packages failed");
					ScheduleRetry(null);
				}
			}
		}
	}

	private async Task WaitForWork(CancellationToken stoppingToken)
	{
		if (_signals.Reader.TryPeek(out _))
		{
			return;
		}

		var now = _timeProvider.GetUtcNow();
		if (_dueAt is { } dueAt && dueAt <= now)
		{
			return;
		}

		using var wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
		var signal = _signals.Reader.WaitToReadAsync(wait.Token).AsTask();
		var tasks = new List<Task> { signal };
		if (_dueAt is { } due)
		{
			tasks.Add(Task.Delay(due - now, _timeProvider, wait.Token));
		}

		await Task.WhenAny(tasks);
		await wait.CancelAsync();
		stoppingToken.ThrowIfCancellationRequested();
	}

	private void Apply(Signal signal)
	{
		switch (signal)
		{
			case SessionSignal(var snapshot):
				var wasSignedIn = _signedIn;
				var wasOffline = _connectivity == ConnectConnectivity.Offline;
				_signedIn = snapshot.Status == ConnectAccountStatus.SignedIn;
				_connectivity = snapshot.Connectivity;

				if (!_signedIn)
				{
					_pendingFullClaim = false;
					_pendingIds.Clear();
					_failedAttempts = 0;
					_dueAt = null;
				}
				else if (!wasSignedIn)
				{
					_pendingFullClaim = true;
					_failedAttempts = 0;
					_dueAt = _timeProvider.GetUtcNow();
				}
				else if (wasOffline && _connectivity == ConnectConnectivity.Ok && HasPending)
				{
					_dueAt = _timeProvider.GetUtcNow();
				}

				break;
			case InstalledSignal(var packageId) when _signedIn:
				_pendingIds.Add(packageId);
				_dueAt = _timeProvider.GetUtcNow();
				break;
			case CatalogRefreshedSignal when _signedIn && HasPending:
				_dueAt = _timeProvider.GetUtcNow();
				break;
		}
	}

	private bool HasPending => _pendingFullClaim || _pendingIds.Count > 0;

	private async Task Claim(CancellationToken cancellationToken)
	{
		if (!_signedIn || !HasPending)
		{
			return;
		}

		if (!_packages.CatalogLoaded)
		{
			_dueAt = _timeProvider.GetUtcNow() + CatalogWaitInterval;
			return;
		}

		var installed = _packages.InstalledPackageIds();
		var ids = _pendingFullClaim
			? installed.ToList()
			: installed.Where(_pendingIds.Contains).ToList();

		if (ids.Count == 0)
		{
			ClearPending();
			return;
		}

		var result = await _platform.ClaimEntitlements(ids, cancellationToken);
		if (result.Success)
		{
			_logger.Information("Claimed {Count} installed Store packages for the signed-in account: {Statuses}",
				ids.Count,
				string.Join(", ", result.Value!.Select(pair => $"{pair.Key}={pair.Value}")));
			ClearPending();
			return;
		}

		switch (result.Failure)
		{
			case StorePlatformFailure.Unavailable or StorePlatformFailure.RetryLater or StorePlatformFailure.Cooldown:
				_logger.Warning("Claiming {Count} installed Store packages failed ({Failure}); retrying later",
					ids.Count,
					result.Failure);
				ScheduleRetry(result.RetryAfter);
				break;
			default:
				_logger.Warning("Claiming {Count} installed Store packages was refused ({Failure})", ids.Count, result.Failure);
				ClearPending();
				break;
		}
	}

	private void ClearPending()
	{
		_pendingFullClaim = false;
		_pendingIds.Clear();
		_failedAttempts = 0;
	}

	private void ScheduleRetry(TimeSpan? retryAfter)
	{
		var delay = RetryDelays[Math.Min(_failedAttempts, RetryDelays.Count - 1)];
		_failedAttempts++;
		if (retryAfter > delay)
		{
			delay = retryAfter.Value;
		}

		_dueAt = _timeProvider.GetUtcNow() + delay;
	}

	private void OnSessionChanged(object? sender, ConnectSessionSnapshot snapshot) =>
		_signals.Writer.TryWrite(new SessionSignal(snapshot));

	private void OnOperationChanged(StoreOperation operation)
	{
		if (operation.State is StoreOperationState.Completed)
		{
			_signals.Writer.TryWrite(new InstalledSignal(operation.PackageId));
		}
	}

	private void OnRefreshChanged(StoreRegistryRefreshRun run)
	{
		if (run.State is StoreRegistryRefreshRunState.Succeeded)
		{
			_signals.Writer.TryWrite(new CatalogRefreshedSignal());
		}
	}

	private abstract record Signal;

	private sealed record SessionSignal(ConnectSessionSnapshot Snapshot) : Signal;

	private sealed record InstalledSignal(string PackageId) : Signal;

	private sealed record CatalogRefreshedSignal : Signal;
}
