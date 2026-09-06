using MacroDeckHost.Application.Adb;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;
using MacroDeckHost.Infrastructure.Adb;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class AdbBackgroundService : HostReadyBackgroundService
{
	private static readonly TimeSpan _reconcileInterval = TimeSpan.FromSeconds(3);

	private static readonly TimeSpan _propertyProbeInterval = TimeSpan.FromSeconds(15);

	private readonly AdbManager _manager;
	private readonly IUiTransport _transport;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;

	private AdbStatus _lastBroadcastStatus = AdbStatus.Disabled;
	private IReadOnlyList<AdbDevice> _lastBroadcastDevices = [];

	public AdbBackgroundService(IHostApplicationLifetime lifetime,
		AdbManager manager,
		IUiTransport transport,
		TimeProvider timeProvider,
		ILogger logger)
		: base(lifetime)
	{
		_manager = manager;
		_transport = transport;
		_timeProvider = timeProvider;
		_logger = logger.ForContext<AdbBackgroundService>();
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		var nextProbe = _timeProvider.GetUtcNow() + _propertyProbeInterval;

		// The manager starts out reporting disabled because nothing has read the stored preferences yet,
		// so the enabled-gate below would park forever on an installation where the user left ADB on.
		// This first pass is what loads them; RefreshNowAsync only reconciles and would not.
		await SafeRun(() => _manager.ApplySettingsAsync(stoppingToken), "initial settings load", stoppingToken);

		while (!stoppingToken.IsCancellationRequested)
		{
			if (!_manager.Status.Enabled)
			{
				await _manager.WaitForEnabledAsync(stoppingToken);
				continue;
			}

			await SafeRun(() => _manager.RefreshNowAsync(stoppingToken), "reconcile", stoppingToken);
			await SafeRun(() => BroadcastIfChanged(stoppingToken), "broadcast", stoppingToken);

			var now = _timeProvider.GetUtcNow();
			if (now >= nextProbe)
			{
				await SafeRun(() => _manager.ProbePropertiesNowAsync(stoppingToken), "property probe", stoppingToken);
				nextProbe = now + _propertyProbeInterval;
			}

			await Task.Delay(_reconcileInterval, stoppingToken);
		}
	}

	public override async Task StopAsync(CancellationToken cancellationToken)
	{
		try
		{
			await base.StopAsync(cancellationToken);
		}
		finally
		{
			// A cancelled host stop must not skip this cleanup boundary, so it runs regardless of how
			// (or whether) the loop above exited. Mirrors IntegrationStartupBackgroundService.StopAsync.
			await _manager.ShutdownAsync();
		}
	}

	private async Task BroadcastIfChanged(CancellationToken stoppingToken)
	{
		var status = _manager.Status;
		var devices = _manager.Devices;

		if (status == _lastBroadcastStatus && devices.SequenceEqual(_lastBroadcastDevices))
		{
			return;
		}

		_lastBroadcastStatus = status;
		_lastBroadcastDevices = devices;

		// Deliberately no payload: the adb pane state (resolved executable path, device serials, ports)
		// is admin-only, but IUiTransport.Send reaches every UI client, including USB-attached
		// devices. Receivers refetch the admin-gated GET instead of trusting anything carried here.
		await _transport.Send(new AdbStateChangedEvent { ChangedAt = _timeProvider.GetUtcNow() }, stoppingToken);
	}

	private async Task SafeRun(Func<Task> action, string what, CancellationToken stoppingToken)
	{
		try
		{
			await action();
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "adb {What} pass failed", what);
		}
	}
}
