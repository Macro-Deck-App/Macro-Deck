using MacroDeckHost.Application.Adb;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Usb;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Infrastructure.Usb.Native;

internal sealed class NativeUsbManager : INativeUsbManager, IDisposable
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IAdbManager _adbManager;
	private readonly IHostListenerState _listenerState;
	private readonly AccessoryCoordinator _android;
	private readonly UsbmuxCoordinator _ios;
	private readonly TimeProvider _time;
	private readonly SemaphoreSlim _gate = new(1, 1);

	private NativeUsbSettings? _settings;

	public NativeUsbManager(IServiceScopeFactory scopeFactory,
		IAdbManager adbManager,
		IHostListenerState listenerState,
		AccessoryCoordinator android,
		UsbmuxCoordinator ios,
		TimeProvider time)
	{
		_scopeFactory = scopeFactory;
		_adbManager = adbManager;
		_listenerState = listenerState;
		_android = android;
		_ios = ios;
		_time = time;
	}

	public NativeUsbStatus Status { get; private set; } = NativeUsbStatus.Initial;

	public async Task ApplySettingsAsync(CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			await ReloadSettingsAsync();
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			await StoreAsync(enabled, null);
			await ReloadSettingsAsync();
			if (!enabled)
			{
				await WaitForStoppedAsync(cancellationToken);
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task PollAsync(CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			var settings = _settings ?? await ReloadSettingsAsync();
			if (!settings.Enabled)
			{
				return;
			}

			var bridgeAvailable = BridgeAvailable;
			var remembered = settings.RememberedSerials.ToHashSet(StringComparer.Ordinal);
			var context = new AccessoryPollContext(bridgeAvailable,
				_adbManager.Devices.Where(device => device.State == AdbDeviceState.Device)
					.Select(device => device.Serial)
					.ToHashSet(StringComparer.Ordinal),
				remembered);
			var now = _time.GetUtcNow();
			// A poll that started finishes, so a phone that linked in it is remembered even if the caller left.
			var linked = await Task.Run(() => _android.Poll(context, now), CancellationToken.None);
			_ios.Poll(bridgeAvailable, now);

			if (linked.Any(device => !remembered.Contains(device.Serial)))
			{
				_settings = await StoreAsync(null, settings.RememberedDevices.Concat(linked).ToList());
			}

			Status = BuildStatus(_settings ?? settings);
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task<NativeUsbPickResult> PickAsync(string deviceId, CancellationToken cancellationToken)
	{
		var result = _android.Pick(deviceId);
		if (result == NativeUsbPickResult.Picked)
		{
			await PollAsync(cancellationToken);
		}

		return result;
	}

	public async Task<bool> ForgetAsync(string serial, CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			var settings = _settings ?? await ReloadSettingsAsync();
			if (!settings.RememberedSerials.Contains(serial, StringComparer.Ordinal))
			{
				return false;
			}

			_settings = await StoreAsync(null,
				settings.RememberedDevices.Where(remembered => remembered.Serial != serial).ToList());
			Status = BuildStatus(_settings);
			return true;
		}
		finally
		{
			_gate.Release();
		}
	}

	public static readonly TimeSpan StopWait = TimeSpan.FromSeconds(3);

	public async Task ShutdownAsync(CancellationToken cancellationToken)
	{
		_ios.StopAll();
		_android.StopAll();
		await WaitForStoppedAsync(cancellationToken);
	}

	private async Task WaitForStoppedAsync(CancellationToken cancellationToken)
	{
		try
		{
			await Task.WhenAll(_android.WhenStopped(), _ios.WhenStopped()).WaitAsync(StopWait, _time, cancellationToken);
		}
		catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
		{
		}
	}

	public void Dispose() => _gate.Dispose();

	private bool BridgeAvailable => LoopbackBridgeDialer.Target(_listenerState.PublicEndpoints) is not null;

	private async Task<NativeUsbSettings> ReloadSettingsAsync()
	{
		using var scope = _scopeFactory.CreateScope();
		var settings = await scope.ServiceProvider.GetRequiredService<IAppPreferenceService>().GetNativeUsb();
		_settings = settings;
		if (!settings.Enabled)
		{
			_android.StopAll();
			_ios.StopAll();
		}

		Status = BuildStatus(settings);
		return settings;
	}

	private async Task<NativeUsbSettings> StoreAsync(bool? enabled, IReadOnlyList<RememberedUsbDevice>? rememberedDevices)
	{
		using var scope = _scopeFactory.CreateScope();
		return await scope.ServiceProvider.GetRequiredService<IAppPreferenceService>()
			.SetNativeUsb(enabled, rememberedDevices);
	}

	private NativeUsbStatus BuildStatus(NativeUsbSettings settings)
		=> new(settings.Enabled,
			settings.Enabled && _android.Available,
			settings.Enabled && _ios.Available,
			BridgeAvailable,
			_listenerState.PublicEndpoints is { HasPublicListener: true, LocalClientEndpoint.Ssl: true },
			settings.Enabled ? [.. _android.Devices, .. _ios.Devices] : [],
			settings.RememberedDevices);
}
