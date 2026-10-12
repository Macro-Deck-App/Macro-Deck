using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Sdk.Android;
using MacroDeckHost.Application.Adb;

namespace MacroDeckHost.Infrastructure.Adb;

internal sealed class InProcessAndroidDevice : IAndroidDevice
{
	private readonly InProcessAndroidDeviceManager _manager;
	private readonly IAdbDeviceOperations _operations;
	private readonly Lock _gate = new();
	private AndroidDeviceInfo _info = new(null, null, null);
	private AndroidDeviceState _state = AndroidDeviceState.Offline;

	public InProcessAndroidDevice(string serial, InProcessAndroidDeviceManager manager, IAdbDeviceOperations operations)
	{
		Serial = serial;
		_manager = manager;
		_operations = operations;
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

	public async Task<AndroidShellResult> ExecuteShellAsync(string command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);
		_manager.ThrowIfLocked(HostOperations.Adb.Shell);
		var output = InProcessAndroidDeviceManager.Unwrap(await _operations.RunShellAsync(Serial, command, cancellationToken));
		var fitted = AdbDeviceCallPolicy.FitToBudget(output);
		return new AndroidShellResult(fitted.ExitCode, fitted.StandardOutput, fitted.StandardError, fitted.Truncated);
	}

	public async Task<AndroidBatteryState> GetBatteryStateAsync(CancellationToken cancellationToken = default)
	{
		var reading = InProcessAndroidDeviceManager.Unwrap(await _operations.GetBatteryAsync(Serial, cancellationToken));
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
		InProcessAndroidDeviceManager.Unwrap(await _operations.PushFileAsync(Serial, localPath, remotePath, cancellationToken));
	}

	public async Task PullFileAsync(string remotePath, string localPath, CancellationToken cancellationToken = default)
	{
		_manager.ThrowIfLocked(HostOperations.Adb.Pull);
		InProcessAndroidDeviceManager.Unwrap(await _operations.PullFileAsync(Serial, remotePath, localPath, cancellationToken));
	}

	public async Task InstallApkAsync(string apkPath, CancellationToken cancellationToken = default)
	{
		_manager.ThrowIfLocked(HostOperations.Adb.Install);
		InProcessAndroidDeviceManager.Unwrap(await _operations.InstallApkAsync(Serial, apkPath, cancellationToken));
	}

	public async Task UninstallPackageAsync(string packageName, CancellationToken cancellationToken = default)
	{
		_manager.ThrowIfLocked(HostOperations.Adb.Uninstall);
		InProcessAndroidDeviceManager.Unwrap(await _operations.UninstallPackageAsync(Serial, packageName, cancellationToken));
	}

	public async Task<bool> IsPackageInstalledAsync(string packageName, CancellationToken cancellationToken = default)
		=> InProcessAndroidDeviceManager.Unwrap(await _operations.IsPackageInstalledAsync(Serial,
			packageName,
			cancellationToken));

	internal void Update(AndroidDeviceInfo info, AndroidDeviceState state)
	{
		lock (_gate)
		{
			_info = info;
			_state = state;
		}
	}
}
