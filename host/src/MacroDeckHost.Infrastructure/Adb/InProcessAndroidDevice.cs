using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Sdk.Android;
using MacroDeckHost.Application.Adb;

namespace MacroDeckHost.Infrastructure.Adb;

internal sealed class InProcessAndroidDevice : IAndroidDevice
{
	private readonly InProcessAndroidDeviceManager _manager;
	private readonly IAdbDeviceOperations _operations;
	private readonly IAdbManager _adbManager;
	private readonly Lock _gate = new();
	private AndroidDeviceInfo _info = new(null, null, null);
	private AndroidDeviceState _state = AndroidDeviceState.Offline;
	private bool _lacksUsbPermission;
	private AndroidDeviceMode _mode;

	public InProcessAndroidDevice(
		string serial,
		InProcessAndroidDeviceManager manager,
		IAdbDeviceOperations operations,
		IAdbManager adbManager)
	{
		Serial = serial;
		_manager = manager;
		_operations = operations;
		_adbManager = adbManager;
	}

	public string Serial { get; }

	public AndroidDeviceInfo Info
	{
		get
		{
			lock (_gate)
			{
				return _info;
			}
		}
	}

	public AndroidDeviceState State
	{
		get
		{
			lock (_gate)
			{
				return _state;
			}
		}
	}

	public bool LacksUsbPermission
	{
		get
		{
			lock (_gate)
			{
				return _lacksUsbPermission;
			}
		}
	}

	public AndroidDeviceMode Mode
	{
		get
		{
			lock (_gate)
			{
				return _mode;
			}
		}
	}

	public async Task<AndroidShellResult> ExecuteShellAsync(
		string command,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);
		_manager.ThrowIfLocked(HostOperations.Adb.Shell);
		var output = InProcessAndroidDeviceManager.Unwrap(
			await _operations.RunShellAsync(Serial, command, cancellationToken));
		var fitted = AdbDeviceCallPolicy.FitToBudget(output);
		return new AndroidShellResult(fitted.ExitCode, fitted.StandardOutput, fitted.StandardError, fitted.Truncated);
	}

	public async Task<AndroidBatteryState> GetBatteryStateAsync(CancellationToken cancellationToken = default)
	{
		var reading = InProcessAndroidDeviceManager.Unwrap(
			await _operations.GetBatteryAsync(Serial, cancellationToken));
		return new AndroidBatteryState
		{
			Level = reading.Level,
			IsCharging = reading.IsPluggedIn,
			Status = reading.Status switch
			{
				AdbBatteryStatus.Charging => AndroidBatteryStatus.Charging,
				AdbBatteryStatus.Discharging => AndroidBatteryStatus.Discharging,
				AdbBatteryStatus.NotCharging => AndroidBatteryStatus.NotCharging,
				AdbBatteryStatus.Full => AndroidBatteryStatus.Full,
				_ => AndroidBatteryStatus.Unknown
			},
			Health = reading.Health switch
			{
				AdbBatteryHealth.Good => AndroidBatteryHealth.Good,
				AdbBatteryHealth.Overheat => AndroidBatteryHealth.Overheat,
				AdbBatteryHealth.Dead => AndroidBatteryHealth.Dead,
				AdbBatteryHealth.OverVoltage => AndroidBatteryHealth.OverVoltage,
				AdbBatteryHealth.Failure => AndroidBatteryHealth.Failure,
				AdbBatteryHealth.Cold => AndroidBatteryHealth.Cold,
				_ => AndroidBatteryHealth.Unknown
			}
		};
	}

	public async Task PushFileAsync(string localPath, string remotePath, CancellationToken cancellationToken = default)
	{
		_manager.ThrowIfLocked(HostOperations.Adb.Push);
		InProcessAndroidDeviceManager.Unwrap(await _operations.PushFileAsync(Serial,
			localPath,
			remotePath,
			cancellationToken));
	}

	public async Task PullFileAsync(string remotePath, string localPath, CancellationToken cancellationToken = default)
	{
		_manager.ThrowIfLocked(HostOperations.Adb.Pull);
		InProcessAndroidDeviceManager.Unwrap(await _operations.PullFileAsync(Serial,
			remotePath,
			localPath,
			cancellationToken));
	}

	public async Task InstallApkAsync(string apkPath, CancellationToken cancellationToken = default)
	{
		_manager.ThrowIfLocked(HostOperations.Adb.Install);
		InProcessAndroidDeviceManager.Unwrap(await _operations.InstallApkAsync(Serial, apkPath, cancellationToken));
	}

	public async Task UninstallPackageAsync(string packageName, CancellationToken cancellationToken = default)
	{
		_manager.ThrowIfLocked(HostOperations.Adb.Uninstall);
		InProcessAndroidDeviceManager.Unwrap(
			await _operations.UninstallPackageAsync(Serial, packageName, cancellationToken));
	}

	public async Task<bool> IsPackageInstalledAsync(string packageName, CancellationToken cancellationToken = default)
	{
		return InProcessAndroidDeviceManager.Unwrap(await _operations.IsPackageInstalledAsync(Serial,
			packageName,
			cancellationToken));
	}

	public async Task SendKeyAsync(AndroidKey key, CancellationToken cancellationToken = default)
	{
		await RunAsync(new AdbKeyEventCommand(Serial, ToAdbKey(key)), cancellationToken);
	}

	public Task TapAsync(int x, int y, CancellationToken cancellationToken = default)
	{
		return RunAsync(new AdbTapCommand(Serial, x, y), cancellationToken);
	}

	public Task SwipeAsync(
		int fromX,
		int fromY,
		int toX,
		int toY,
		TimeSpan duration,
		CancellationToken cancellationToken = default)
	{
		return RunAsync(new AdbSwipeCommand(Serial, fromX, fromY, toX, toY, ToMilliseconds(duration)),
			cancellationToken);
	}

	public Task InputTextAsync(string text, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(text);
		return RunAsync(new AdbInputTextCommand(Serial, text), cancellationToken);
	}

	public Task StartAppAsync(string packageName, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(packageName);
		return RunAsync(new AdbStartAppCommand(Serial, packageName), cancellationToken);
	}

	public Task ForceStopAppAsync(string packageName, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(packageName);
		return RunAsync(new AdbForceStopAppCommand(Serial, packageName), cancellationToken);
	}

	public Task OpenUriAsync(string uri, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(uri);
		return RunAsync(new AdbOpenUriCommand(Serial, uri), cancellationToken);
	}

	public async Task RebootAsync(
		AndroidRebootMode mode = AndroidRebootMode.Normal,
		CancellationToken cancellationToken = default)
	{
		await RunAsync(new AdbRebootCommand(Serial, ToAdbRebootMode(mode)), cancellationToken);
	}

	public async Task<byte[]> CaptureScreenshotAsync(CancellationToken cancellationToken = default)
	{
		return InProcessAndroidDeviceManager.Unwrap(
			await _adbManager.CaptureScreenshotAsync(Serial, cancellationToken));
	}

	public async Task<AndroidDeviceStatus?> GetStatusAsync(CancellationToken cancellationToken = default)
	{
		var properties = await _adbManager.GetPropertiesAsync(Serial, cancellationToken);
		return properties is null
			? null
			: new AndroidDeviceStatus
			{
				IsScreenOn = properties.ScreenOn,
				IsLocked = properties.Locked,
				ForegroundPackage = properties.ForegroundPackage
			};
	}

	internal void Update(
		AndroidDeviceInfo info,
		AndroidDeviceState state,
		bool lacksUsbPermission,
		AndroidDeviceMode mode)
	{
		lock (_gate)
		{
			_info = info;
			_state = state;
			_lacksUsbPermission = lacksUsbPermission;
			_mode = mode;
		}
	}

	internal static int ToMilliseconds(TimeSpan duration)
	{
		return (int)Math.Clamp(Math.Round(duration.TotalMilliseconds), int.MinValue, int.MaxValue);
	}

	internal static AdbKey ToAdbKey(AndroidKey key)
	{
		return key switch
		{
			AndroidKey.Home => AdbKey.Home,
			AndroidKey.Back => AdbKey.Back,
			AndroidKey.Enter => AdbKey.Enter,
			AndroidKey.Menu => AdbKey.Menu,
			AndroidKey.Search => AdbKey.Search,
			AndroidKey.VolumeUp => AdbKey.VolumeUp,
			AndroidKey.VolumeDown => AdbKey.VolumeDown,
			AndroidKey.VolumeMute => AdbKey.VolumeMute,
			AndroidKey.MediaPlayPause => AdbKey.MediaPlayPause,
			AndroidKey.MediaNext => AdbKey.MediaNext,
			AndroidKey.MediaPrevious => AdbKey.MediaPrevious,
			AndroidKey.Power => AdbKey.Power,
			AndroidKey.Sleep => AdbKey.Sleep,
			AndroidKey.Wakeup => AdbKey.Wakeup,
			AndroidKey.AppSwitch => AdbKey.AppSwitch,
			AndroidKey.DpadUp => AdbKey.DpadUp,
			AndroidKey.DpadDown => AdbKey.DpadDown,
			AndroidKey.DpadLeft => AdbKey.DpadLeft,
			AndroidKey.DpadRight => AdbKey.DpadRight,
			AndroidKey.DpadCenter => AdbKey.DpadCenter,
			_ => throw new AndroidDeviceException(AndroidDeviceErrorCode.InvalidArgument, $"Unknown key {key}.")
		};
	}

	internal static AdbRebootMode ToAdbRebootMode(AndroidRebootMode mode)
	{
		return mode switch
		{
			AndroidRebootMode.Normal => AdbRebootMode.Normal,
			AndroidRebootMode.Recovery => AdbRebootMode.Recovery,
			AndroidRebootMode.Bootloader => AdbRebootMode.Bootloader,
			_ => throw new AndroidDeviceException(AndroidDeviceErrorCode.InvalidArgument,
				$"Unknown reboot mode {mode}.")
		};
	}

	private async Task RunAsync(AdbCommand command, CancellationToken cancellationToken)
	{
		InProcessAndroidDeviceManager.Unwrap(await _adbManager.ExecuteAsync(command, cancellationToken));
	}
}
