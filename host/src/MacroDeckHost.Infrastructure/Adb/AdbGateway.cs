using MacroDeckHost.Application.Adb;
using MacroDeckHost.Integrations.Adb;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Adb;

internal sealed class AdbGateway : IAdbGateway, IDisposable
{
	private const string DisabledMessage = "ADB is turned off in Settings > ADB.";

	private readonly IAdbManager _adbManager;
	private readonly ILogger _logger;
	private bool _disposed;

	internal AdbGateway(IAdbManager adbManager, ILogger logger)
	{
		_adbManager = adbManager;
		_logger = logger.ForContext<AdbGateway>();
		_adbManager.DeviceChanged += OnManagerDeviceChanged;
	}

	public bool IsEnabled => _adbManager.Status.Enabled;

	public IReadOnlyList<AdbGatewayDevice> Devices => _adbManager.Devices.Select(MapDevice).ToList();

	public string? DefaultDeviceSerial => _adbManager.Status.DefaultDeviceSerial;

	public event EventHandler<AdbGatewayDeviceChange>? DeviceChanged;

	public Task<AdbGatewayResult> SendKeyAsync(string? serial, AdbGatewayKey key, CancellationToken cancellationToken)
		=> RunCommandAsync(new AdbKeyEventCommand(serial ?? string.Empty, MapKey(key)), cancellationToken);

	public Task<AdbGatewayResult> StartAppAsync(string? serial, string package, CancellationToken cancellationToken)
		=> RunCommandAsync(new AdbStartAppCommand(serial ?? string.Empty, package), cancellationToken);

	public Task<AdbGatewayResult> ForceStopAppAsync(string? serial, string package, CancellationToken cancellationToken)
		=> RunCommandAsync(new AdbForceStopAppCommand(serial ?? string.Empty, package), cancellationToken);

	public Task<AdbGatewayResult> OpenUriAsync(string? serial, string uri, CancellationToken cancellationToken)
		=> RunCommandAsync(new AdbOpenUriCommand(serial ?? string.Empty, uri), cancellationToken);

	public Task<AdbGatewayResult> InputTextAsync(string? serial, string text, CancellationToken cancellationToken)
		=> RunCommandAsync(new AdbInputTextCommand(serial ?? string.Empty, text), cancellationToken);

	public Task<AdbGatewayResult> TapAsync(string? serial, int x, int y, CancellationToken cancellationToken)
		=> RunCommandAsync(new AdbTapCommand(serial ?? string.Empty, x, y), cancellationToken);

	public Task<AdbGatewayResult> SwipeAsync(string? serial,
		int x1,
		int y1,
		int x2,
		int y2,
		int durationMs,
		CancellationToken cancellationToken)
		=> RunCommandAsync(new AdbSwipeCommand(serial ?? string.Empty, x1, y1, x2, y2, durationMs), cancellationToken);

	public Task<AdbGatewayResult> RebootAsync(string? serial,
		AdbGatewayRebootMode mode,
		CancellationToken cancellationToken)
		=> RunCommandAsync(new AdbRebootCommand(serial ?? string.Empty, MapRebootMode(mode)), cancellationToken);

	public async Task<(AdbGatewayResult Result, byte[]? Png)> CaptureScreenshotAsync(string? serial,
		CancellationToken cancellationToken)
	{
		if (!IsEnabled)
		{
			return (AdbGatewayResult.Fail(AdbGatewayFailureCode.Disabled, DisabledMessage), null);
		}

		var result = await _adbManager.CaptureScreenshotAsync(serial, cancellationToken);
		return result.Success
			? (AdbGatewayResult.Ok(), result.Data)
			: (AdbGatewayResult.Fail(MapFailureCode(result.Error!.Value), result.ErrorMessage), null);
	}

	public async Task<AdbGatewayProperties?> GetPropertiesAsync(string? serial, CancellationToken cancellationToken)
	{
		if (!IsEnabled)
		{
			return null;
		}

		var properties = await _adbManager.GetPropertiesAsync(serial, cancellationToken);
		return properties is null
			? null
			: new AdbGatewayProperties(properties.BatteryLevel,
				properties.ScreenOn,
				properties.Locked,
				properties.ForegroundPackage);
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_adbManager.DeviceChanged -= OnManagerDeviceChanged;
	}

	private async Task<AdbGatewayResult> RunCommandAsync(AdbCommand command, CancellationToken cancellationToken)
	{
		if (!IsEnabled)
		{
			return AdbGatewayResult.Fail(AdbGatewayFailureCode.Disabled, DisabledMessage);
		}

		var result = await _adbManager.ExecuteAsync(command, cancellationToken);
		return result.Success
			? AdbGatewayResult.Ok()
			: AdbGatewayResult.Fail(MapFailureCode(result.Error!.Value), result.ErrorMessage);
	}

	private void OnManagerDeviceChanged(object? sender, AdbDeviceChange change)
	{
		var handler = DeviceChanged;
		if (handler is null)
		{
			return;
		}

		var mapped = new AdbGatewayDeviceChange(MapChangeKind(change.Kind), MapDevice(change.Device));
		foreach (var subscriber in handler.GetInvocationList())
		{
			try
			{
				((EventHandler<AdbGatewayDeviceChange>)subscriber).Invoke(this, mapped);
			}
			catch (Exception ex)
			{
				_logger.Warning(ex, "A gateway DeviceChanged subscriber threw for {Serial}", mapped.Device.Serial);
			}
		}
	}

	private static AdbGatewayDevice MapDevice(AdbDevice device)
		=> new(device.Serial, MapState(device.State), device.Model, device.Manufacturer, device.Tunnel is not null);

	private static AdbGatewayDeviceState MapState(AdbDeviceState state) => state switch
	{
		AdbDeviceState.Unknown => AdbGatewayDeviceState.Unknown,
		AdbDeviceState.Device => AdbGatewayDeviceState.Device,
		AdbDeviceState.Unauthorized => AdbGatewayDeviceState.Unauthorized,
		AdbDeviceState.Offline => AdbGatewayDeviceState.Offline,
		AdbDeviceState.Authorizing => AdbGatewayDeviceState.Authorizing,
		AdbDeviceState.NoPermissions => AdbGatewayDeviceState.NoPermissions,
		AdbDeviceState.Bootloader => AdbGatewayDeviceState.Bootloader,
		AdbDeviceState.Recovery => AdbGatewayDeviceState.Recovery,
		AdbDeviceState.Sideload => AdbGatewayDeviceState.Sideload,
		AdbDeviceState.Disconnected => AdbGatewayDeviceState.Disconnected,
		_ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown adb device state")
	};

	private static AdbGatewayDeviceChangeKind MapChangeKind(AdbDeviceChangeKind kind) => kind switch
	{
		AdbDeviceChangeKind.Connected => AdbGatewayDeviceChangeKind.Connected,
		AdbDeviceChangeKind.Disconnected => AdbGatewayDeviceChangeKind.Disconnected,
		AdbDeviceChangeKind.Authorized => AdbGatewayDeviceChangeKind.Authorized,
		AdbDeviceChangeKind.Unauthorized => AdbGatewayDeviceChangeKind.Unauthorized,
		AdbDeviceChangeKind.Online => AdbGatewayDeviceChangeKind.Online,
		AdbDeviceChangeKind.Offline => AdbGatewayDeviceChangeKind.Offline,
		_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown adb device change kind")
	};

	private static AdbKey MapKey(AdbGatewayKey key) => key switch
	{
		AdbGatewayKey.Home => AdbKey.Home,
		AdbGatewayKey.Back => AdbKey.Back,
		AdbGatewayKey.Enter => AdbKey.Enter,
		AdbGatewayKey.Menu => AdbKey.Menu,
		AdbGatewayKey.Search => AdbKey.Search,
		AdbGatewayKey.VolumeUp => AdbKey.VolumeUp,
		AdbGatewayKey.VolumeDown => AdbKey.VolumeDown,
		AdbGatewayKey.VolumeMute => AdbKey.VolumeMute,
		AdbGatewayKey.MediaPlayPause => AdbKey.MediaPlayPause,
		AdbGatewayKey.MediaNext => AdbKey.MediaNext,
		AdbGatewayKey.MediaPrevious => AdbKey.MediaPrevious,
		AdbGatewayKey.Power => AdbKey.Power,
		AdbGatewayKey.Sleep => AdbKey.Sleep,
		AdbGatewayKey.Wakeup => AdbKey.Wakeup,
		AdbGatewayKey.AppSwitch => AdbKey.AppSwitch,
		AdbGatewayKey.DpadUp => AdbKey.DpadUp,
		AdbGatewayKey.DpadDown => AdbKey.DpadDown,
		AdbGatewayKey.DpadLeft => AdbKey.DpadLeft,
		AdbGatewayKey.DpadRight => AdbKey.DpadRight,
		AdbGatewayKey.DpadCenter => AdbKey.DpadCenter,
		_ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown adb gateway key")
	};

	private static AdbRebootMode MapRebootMode(AdbGatewayRebootMode mode) => mode switch
	{
		AdbGatewayRebootMode.Normal => AdbRebootMode.Normal,
		AdbGatewayRebootMode.Recovery => AdbRebootMode.Recovery,
		AdbGatewayRebootMode.Bootloader => AdbRebootMode.Bootloader,
		_ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown adb gateway reboot mode")
	};

	private static AdbGatewayFailureCode MapFailureCode(AdbFailureCode code) => code switch
	{
		AdbFailureCode.Disabled => AdbGatewayFailureCode.Disabled,
		AdbFailureCode.ExecutableNotFound => AdbGatewayFailureCode.ExecutableNotFound,
		AdbFailureCode.ServerUnreachable => AdbGatewayFailureCode.ServerUnreachable,
		AdbFailureCode.DeviceNotFound => AdbGatewayFailureCode.DeviceNotFound,
		AdbFailureCode.DeviceUnauthorized => AdbGatewayFailureCode.DeviceUnauthorized,
		AdbFailureCode.DeviceOffline => AdbGatewayFailureCode.DeviceOffline,
		AdbFailureCode.Timeout => AdbGatewayFailureCode.Timeout,
		AdbFailureCode.InvalidParameter => AdbGatewayFailureCode.InvalidParameter,
		AdbFailureCode.CommandFailed => AdbGatewayFailureCode.CommandFailed,
		AdbFailureCode.Unsupported => AdbGatewayFailureCode.Unsupported,
		_ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown adb failure code")
	};
}
