using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Infrastructure.HostLocking;
using MacroDeckHost.Integrations.System.Lock;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class HostLockStateBackgroundService : HostReadyBackgroundService
{
	private static readonly TimeSpan _tickInterval = TimeSpan.FromSeconds(1);

	private readonly ILockStateReader _reader;
	private readonly ILockStateWatcher _watcher;
	private readonly HostLockState _state;
	private readonly Func<Task<LockScreenSettings>> _readLockScreenSettings;
	private readonly IMediator _mediator;
	private readonly ILogger _logger;
	private readonly SemaphoreSlim _applyGate = new(1, 1);

	private bool? _lastLocked;

	public HostLockStateBackgroundService(
		IHostApplicationLifetime lifetime,
		HostLockState state,
		IServiceScopeFactory scopeFactory,
		IMediator mediator,
		ILogger logger)
		: this(lifetime,
			LockStateReaderFactory.Create(),
			LockStateWatcherFactory.Create(),
			state,
			ReadThroughScope(scopeFactory),
			mediator,
			logger)
	{
	}

	// Test seam: lets unit tests inject a fake ILockStateReader/ILockStateWatcher instead of the platform
	// factories.
	internal HostLockStateBackgroundService(
		IHostApplicationLifetime lifetime,
		ILockStateReader reader,
		ILockStateWatcher watcher,
		HostLockState state,
		Func<Task<LockScreenSettings>> readLockScreenSettings,
		IMediator mediator,
		ILogger logger)
		: base(lifetime)
	{
		_reader = reader;
		_watcher = watcher;
		_state = state;
		_readLockScreenSettings = readLockScreenSettings;
		_mediator = mediator;
		_logger = logger.ForContext<HostLockStateBackgroundService>();
	}

	// IAppPreferenceService is scoped and this is a singleton hosted service, so the preference is
	// read through a fresh scope per tick rather than captured in the constructor.
	private static Func<Task<LockScreenSettings>> ReadThroughScope(IServiceScopeFactory scopeFactory)
		=> async () =>
		{
			await using var scope = scopeFactory.CreateAsyncScope();
			return await scope.ServiceProvider.GetRequiredService<IAppPreferenceService>().GetLockScreen();
		};

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		_state.IsSupported = _reader.IsSupported;

		if (!_reader.IsSupported)
		{
			_logger.Information("Lock-state detection is unavailable: {Reason}", _reader.UnsupportedReason);
			return;
		}

		await Task.WhenAll(PollLoop(stoppingToken), ConsumeWatcher(stoppingToken));
	}

	private async Task PollLoop(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(_tickInterval);
		do
		{
			await SafeTick(stoppingToken);
		} while (await timer.WaitForNextTickAsync(stoppingToken));
	}

	private async Task SafeTick(CancellationToken stoppingToken)
	{
		try
		{
			await Tick(stoppingToken);
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Lock-state tick failed");
		}
	}

	internal Task Tick(CancellationToken cancellationToken) => Apply(_reader.IsLocked, cancellationToken);

	// The watcher loop returns immediately when unsupported: enumerating a watcher that can never yield
	// would still be correct, but skipping WatchAsync entirely avoids depending on that for platforms
	// where the poll is the only mechanism (Windows/Linux, and macOS without a pumped run loop).
	internal async Task ConsumeWatcher(CancellationToken stoppingToken)
	{
		if (!_watcher.IsSupported)
		{
			return;
		}

		try
		{
			await foreach (var locked in _watcher.WatchAsync(stoppingToken))
			{
				await Apply(() => locked, stoppingToken);
			}
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
		}
		catch (Exception ex)
		{
			// A failed watcher must not take the poll down with it - the poll keeps running as the
			// fallback, so this loop simply ends rather than resubscribing.
			_logger.Error(ex, "Lock-state watcher failed; falling back to polling only");
		}
	}

	// The value is produced inside the gate, not passed in: a poll that read the sensor before a signalled
	// change won the gate would otherwise republish the state the signal just superseded, putting the lock
	// screen back for a further tick - the very symptom this fixes. Holding the publish inside the gate
	// totally orders the two producers without risking a stall, because IUiTransport.Send only ever does a
	// non-blocking TryWrite per connection.
	private async Task Apply(Func<bool?> read, CancellationToken cancellationToken)
	{
		await _applyGate.WaitAsync(cancellationToken);
		try
		{
			if (read() is not { } locked || locked == _lastLocked)
			{
				return;
			}

			_lastLocked = locked;
			_state.IsLocked = locked;
			LockStateSnapshot.Current.Set(locked);

			var lockScreen = await _readLockScreenSettings();
			await _mediator.Publish(
				new HostLockStateChangedNotification(locked, lockScreen.Enabled, _state.IsSupported),
				cancellationToken);
		}
		finally
		{
			_applyGate.Release();
		}
	}

	public override void Dispose()
	{
		// _applyGate is deliberately not disposed: either loop may still be unwinding on another thread,
		// and a SemaphoreSlim whose AvailableWaitHandle was never taken holds nothing that needs releasing.
		_watcher.Dispose();
		base.Dispose();
	}
}
