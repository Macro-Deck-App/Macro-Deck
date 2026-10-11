using MacroDeck.Sdk.Android;

namespace MacroDeckHost.Tests.UnitTests.Adb;

internal sealed class ScriptedAndroidDevices : IAndroidDeviceManager
{
	private readonly Lock _sync = new();
	private readonly List<ScriptedAndroidDevice> _devices = [];
	private AndroidDeviceAccess _access = AndroidDeviceAccess.Available;

	public event EventHandler<AndroidDeviceEventArgs>? DeviceConnected;

	public event EventHandler<AndroidDeviceEventArgs>? DeviceDisconnected;

	public event EventHandler<AndroidDeviceEventArgs>? DeviceStateChanged;

	public event EventHandler? AccessChanged;

	public AndroidDeviceAccess Access
	{
		get
		{
			lock (_sync)
			{
				return _access;
			}
		}
	}

	public IReadOnlyCollection<IAndroidDevice> Devices
	{
		get
		{
			lock (_sync)
			{
				return _access == AndroidDeviceAccess.Available ? [.. _devices] : [];
			}
		}
	}

	public IAndroidDevice? FindDevice(string serial)
		=> Devices.FirstOrDefault(device => device.Serial == serial);

	public Task<IAndroidDevice> ConnectAsync(string address, CancellationToken cancellationToken = default)
		=> throw new NotSupportedException();

	public ScriptedAndroidDevice Attach(string serial,
		string? stableId,
		bool raise = true,
		AndroidBatteryState? battery = null)
	{
		var device = new ScriptedAndroidDevice(serial, stableId);
		if (battery is not null)
		{
			device.Battery = battery;
		}

		lock (_sync)
		{
			_devices.Add(device);
		}

		if (raise)
		{
			DeviceConnected?.Invoke(this, new AndroidDeviceEventArgs(device, null));
		}

		return device;
	}

	public void Detach(string serial, bool raise = true)
	{
		ScriptedAndroidDevice device;
		lock (_sync)
		{
			device = _devices.Single(candidate => candidate.Serial == serial);
			_devices.Remove(device);
		}

		if (raise)
		{
			DeviceDisconnected?.Invoke(this, new AndroidDeviceEventArgs(device, null));
		}
	}

	public void SetAccess(AndroidDeviceAccess access)
	{
		List<ScriptedAndroidDevice> withdrawn;
		lock (_sync)
		{
			withdrawn = access == AndroidDeviceAccess.Available ? [] : [.. _devices];
			_access = access;
		}

		AccessChanged?.Invoke(this, EventArgs.Empty);
		foreach (var device in withdrawn)
		{
			DeviceDisconnected?.Invoke(this, new AndroidDeviceEventArgs(device, null));
		}
	}

	public void RaiseStateChanged(ScriptedAndroidDevice device, AndroidDeviceState previous)
		=> DeviceStateChanged?.Invoke(this, new AndroidDeviceEventArgs(device, previous));
}

internal sealed class ScriptedAndroidDevice(string serial, string? stableId) : IAndroidDevice
{
	private readonly Lock _sync = new();
	private readonly List<string> _commands = [];

	public string Serial { get; } = serial;

	public AndroidDeviceInfo Info { get; set; } = new("Pixel_8", "Google", "shiba") { StableId = stableId };

	public AndroidDeviceState State { get; set; } = AndroidDeviceState.Online;

	public AndroidBatteryState Battery { get; set; } = new()
	{
		Level = 50, IsCharging = false, Status = AndroidBatteryStatus.Discharging, Health = AndroidBatteryHealth.Good
	};

	public string Characteristics { get; set; } = "default";

	public AndroidDeviceErrorCode? ShellFailure { get; set; }

	public IReadOnlyList<string> Commands
	{
		get
		{
			lock (_sync)
			{
				return [.. _commands];
			}
		}
	}

	public Task<AndroidShellResult> ExecuteShellAsync(string command, CancellationToken cancellationToken = default)
	{
		lock (_sync)
		{
			_commands.Add(command);
		}

		return ShellFailure is { } failure
			? Task.FromException<AndroidShellResult>(new AndroidDeviceException(failure, "refused"))
			: Task.FromResult(new AndroidShellResult(0, $"{Characteristics}\n", string.Empty, false));
	}

	public Task<AndroidBatteryState> GetBatteryStateAsync(CancellationToken cancellationToken = default)
		=> Task.FromResult(Battery);

	public Task PushFileAsync(string localPath, string remotePath, CancellationToken cancellationToken = default)
		=> throw new NotSupportedException();

	public Task PullFileAsync(string remotePath, string localPath, CancellationToken cancellationToken = default)
		=> throw new NotSupportedException();

	public Task InstallApkAsync(string apkPath, CancellationToken cancellationToken = default)
		=> throw new NotSupportedException();

	public Task UninstallPackageAsync(string packageName, CancellationToken cancellationToken = default)
		=> throw new NotSupportedException();

	public Task<bool> IsPackageInstalledAsync(string packageName, CancellationToken cancellationToken = default)
		=> throw new NotSupportedException();
}
