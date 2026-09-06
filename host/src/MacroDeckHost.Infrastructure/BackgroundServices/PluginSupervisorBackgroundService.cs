using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.Plugins.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class PluginSupervisorBackgroundService : HostReadyBackgroundService
{
	private static readonly TimeSpan _tickInterval = TimeSpan.FromSeconds(1);
	private static readonly TimeSpan _shutdownBudget = PluginShutdownBudgets.SupervisorPluginBudget;

	private readonly IPluginSupervisor _supervisor;
	private readonly IPluginOrphanReaper _orphanReaper;
	private readonly IRemotePluginIntegrationRegistrar _registrar;
	private readonly IHostListenerState _listenerState;
	private readonly IHostApplicationLifetime _lifetime;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILogger _logger;

	private Task? _stopAllTask;
	private CancellationTokenRegistration _stoppingRegistration;

	public PluginSupervisorBackgroundService(
		IPluginSupervisor supervisor,
		IPluginOrphanReaper orphanReaper,
		IRemotePluginIntegrationRegistrar registrar,
		IHostListenerState listenerState,
		IHostApplicationLifetime lifetime,
		IServiceScopeFactory scopeFactory,
		ILogger logger)
		: base(lifetime)
	{
		_supervisor = supervisor;
		_orphanReaper = orphanReaper;
		_registrar = registrar;
		_listenerState = listenerState;
		_lifetime = lifetime;
		_scopeFactory = scopeFactory;
		_logger = logger.ForContext<PluginSupervisorBackgroundService>();
	}

	public override Task StartAsync(CancellationToken cancellationToken)
	{
		// Registered here rather than in ExecuteWhenReady, which never runs when the host is stopped
		// before it reports started; a host that never started still has plugins to stop.
		_stoppingRegistration = _lifetime.ApplicationStopping.Register(OnStopping);

		return base.StartAsync(cancellationToken);
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		// An orphan from a previous session still holds its health port and hammers the session
		// endpoint, so reconciling first would reconcile against a world that is about to change.
		try
		{
			await _orphanReaper.ReapAsync(stoppingToken);
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Reaping orphaned plugin processes from a previous host session failed");
		}

		// Catches up state that predates the enrollment-claim guard and the install-time revoke: a
		// plugin id that is both installed and (still) enrolled. Runs before anything below so a
		// stale enrollment cannot win a race against the launch latch that guards new sessions.
		// Best-effort for the same reason as the registrar call below - one plugin's stale state must
		// not stop the supervisor from starting.
		try
		{
			using var reconcileScope = _scopeFactory.CreateScope();
			var reconciler = reconcileScope.ServiceProvider.GetRequiredService<IPluginIdentityReconciler>();
			await reconciler.ReconcileAsync(stoppingToken);
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Startup plugin identity reconciliation failed");
		}

		// Section D of #413 step 5: an installed-but-not-running managed plugin still has to appear as
		// an integration (bound buttons resolving, its page listed) before its process ever launches.
		// Best-effort - a failure here must not stop the supervisor's own reconcile loop from starting.
		try
		{
			await _registrar.RegisterInstalledButStoppedAsync(stoppingToken);
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Registering detached adapters for installed-but-stopped plugins failed");
		}

		using var timer = new PeriodicTimer(_tickInterval);
		do
		{
			if (_listenerState.LoopbackPort is null)
			{
				continue;
			}

			await SafeTick(stoppingToken);
		} while (await timer.WaitForNextTickAsync(stoppingToken));
	}

	private async Task SafeTick(CancellationToken stoppingToken)
	{
		try
		{
			await _supervisor.Reconcile(stoppingToken);
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Plugin supervisor reconcile tick failed");
		}

		try
		{
			await _registrar.UnregisterVanishedInstallationsAsync(stoppingToken);
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Sweeping integrations for removed plugin installations failed");
		}
	}

	private void OnStopping() => _stopAllTask ??= StopAllWithBudget();

	public override async Task StopAsync(CancellationToken cancellationToken)
	{
		await base.StopAsync(cancellationToken);

		// Bounded by StopAllWithBudget's own CTS, so a plugin that will not die cannot hang shutdown.
		if (_stopAllTask is { } pending)
		{
			await pending;
		}

		_stoppingRegistration.Dispose();
	}

	private async Task StopAllWithBudget()
	{
		try
		{
			using var cts = new CancellationTokenSource(_shutdownBudget);
			await _supervisor.StopAll(PluginStopReason.HostShutdown, cts.Token);
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Stopping managed plugins during host shutdown failed");
		}
	}
}
