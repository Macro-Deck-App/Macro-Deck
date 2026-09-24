using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;
using MacroDeckHost.Application.Usb;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class NativeUsbBackgroundService : HostReadyBackgroundService
{
	private static readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(3);

	private readonly INativeUsbManager _manager;
	private readonly IUiTransport _transport;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;

	private NativeUsbStatus _lastBroadcast = NativeUsbStatus.Initial;

	public NativeUsbBackgroundService(IHostApplicationLifetime lifetime,
		INativeUsbManager manager,
		IUiTransport transport,
		TimeProvider timeProvider,
		ILogger logger)
		: base(lifetime)
	{
		_manager = manager;
		_transport = transport;
		_timeProvider = timeProvider;
		_logger = logger.ForContext<NativeUsbBackgroundService>();
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		try
		{
			await _manager.ApplySettingsAsync(stoppingToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.Warning(ex, "Loading the USB connection settings failed");
		}

		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				await _manager.PollAsync(stoppingToken);
				await BroadcastIfChanged(stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				_logger.Warning(ex, "USB connection poll failed");
			}

			await Task.Delay(_pollInterval, _timeProvider, stoppingToken);
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
			await _manager.ShutdownAsync(cancellationToken);
		}
	}

	private async Task BroadcastIfChanged(CancellationToken stoppingToken)
	{
		var status = _manager.Status;
		if (SameStatus(status, _lastBroadcast))
		{
			return;
		}

		_lastBroadcast = status;
		// No payload: serials are admin-only, and this event reaches every UI client.
		await _transport.Send(new NativeUsbStateChangedEvent { ChangedAt = _timeProvider.GetUtcNow() }, stoppingToken);
	}

	private static bool SameStatus(NativeUsbStatus left, NativeUsbStatus right)
		=> left.Enabled == right.Enabled &&
			left.AndroidAvailable == right.AndroidAvailable &&
			left.IosAvailable == right.IosAvailable &&
			left.BridgeAvailable == right.BridgeAvailable &&
			left.HttpsOnly == right.HttpsOnly &&
			left.Devices.SequenceEqual(right.Devices) &&
			left.RememberedDevices.SequenceEqual(right.RememberedDevices);
}
