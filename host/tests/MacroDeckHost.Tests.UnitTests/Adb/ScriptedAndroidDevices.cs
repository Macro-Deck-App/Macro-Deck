using System.Globalization;
using MacroDeck.Sdk.Android;

namespace MacroDeckHost.Tests.UnitTests.Adb;

internal sealed class ScriptedAndroidDevices : IAndroidDeviceManager
{
	private readonly Lock _sync = new();
	private readonly List<ScriptedAndroidDevice> _devices = [];
	private readonly Dictionary<string, ScriptedAndroidDevice> _known = new(StringComparer.Ordinal);
	private readonly List<string> _calls = [];
	private AndroidDeviceAccess _access = AndroidDeviceAccess.Available;

	public IReadOnlyList<string> Calls
	{
		get
		{
			lock (_sync)
			{
				return [.. _calls];
			}
		}
	}

	public bool IsAdbUnavailable { get; set; }

	public string? DefaultSerial { get; set; }

	public IAndroidDevice? DefaultDevice
	{
		get
		{
			lock (_sync)
			{
				if (_access != AndroidDeviceAccess.Available)
				{
					return null;
				}

				if (DefaultSerial is { } serial)
				{
					return _known.GetValueOrDefault(serial);
				}

				return _devices.Count == 1 ? _devices[0] : null;
			}
		}
	}

	internal void Record(string call)
	{
		lock (_sync)
		{
			_calls.Add(call);
		}
	}

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
	{
		return Devices.FirstOrDefault(device => device.Serial == serial);
	}

	public Task<IAndroidDevice> ConnectAsync(string address, CancellationToken cancellationToken = default)
	{
		throw new NotSupportedException();
	}

	public ScriptedAndroidDevice Attach(
		string serial,
		string? stableId,
		bool raise = true,
		AndroidBatteryState? battery = null,
		AndroidDeviceState state = AndroidDeviceState.Online,
		string? model = "Pixel_8",
		bool lacksUsbPermission = false,
		AndroidDeviceMode mode = AndroidDeviceMode.Normal)
	{
		ScriptedAndroidDevice device;
		lock (_sync)
		{
			if (!_known.TryGetValue(serial, out device!))
			{
				device = new ScriptedAndroidDevice(serial, stableId) { Owner = this };
				_known[serial] = device;
			}

			device.State = state;
			device.LacksUsbPermission = lacksUsbPermission;
			device.Mode = mode;
			device.Info = device.Info with { Model = model };
			if (battery is not null)
			{
				device.Battery = battery;
			}

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
	{
		DeviceStateChanged?.Invoke(this, new AndroidDeviceEventArgs(device, previous));
	}

	public void SetState(
		string serial,
		AndroidDeviceState state,
		bool lacksUsbPermission = false,
		AndroidDeviceMode mode = AndroidDeviceMode.Normal)
	{
		ScriptedAndroidDevice device;
		lock (_sync)
		{
			device = _devices.Single(candidate => candidate.Serial == serial);
		}

		var previous = device.State;
		device.State = state;
		device.LacksUsbPermission = lacksUsbPermission;
		device.Mode = mode;
		DeviceStateChanged?.Invoke(this, new AndroidDeviceEventArgs(device, previous));
	}
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
	{
		return BatteryFailure is { } failure
			? Task.FromException<AndroidBatteryState>(failure)
			: Task.FromResult(Battery);
	}

	public Task PushFileAsync(string localPath, string remotePath, CancellationToken cancellationToken = default)
	{
		throw new NotSupportedException();
	}

	public Task PullFileAsync(string remotePath, string localPath, CancellationToken cancellationToken = default)
	{
		throw new NotSupportedException();
	}

	public Task InstallApkAsync(string apkPath, CancellationToken cancellationToken = default)
	{
		throw new NotSupportedException();
	}

	public Task UninstallPackageAsync(string packageName, CancellationToken cancellationToken = default)
	{
		throw new NotSupportedException();
	}

	public Task<bool> IsPackageInstalledAsync(string packageName, CancellationToken cancellationToken = default)
	{
		throw new NotSupportedException();
	}

	internal ScriptedAndroidDevices? Owner { get; init; }

	public bool LacksUsbPermission { get; set; }

	public AndroidDeviceMode Mode { get; set; }

	public AndroidDeviceException? NextFailure { get; set; }

	public AndroidDeviceException? BatteryFailure { get; set; }

	public byte[] Screenshot { get; set; } = [1, 2, 3];

	public AndroidDeviceStatus? Status { get; set; }

	public Task SendKeyAsync(AndroidKey key, CancellationToken cancellationToken = default)
	{
		return Run($"SendKey:{key}");
	}

	public Task TapAsync(int x, int y, CancellationToken cancellationToken = default)
	{
		return Run($"Tap:{Invariant(x)}:{Invariant(y)}");
	}

	public Task SwipeAsync(
		int fromX,
		int fromY,
		int toX,
		int toY,
		TimeSpan duration,
		CancellationToken cancellationToken = default)
	{
		return Run($"Swipe:{Invariant(fromX)}:{Invariant(fromY)}:{Invariant(toX)}:{Invariant(toY)}:"
			+ Invariant((int)duration.TotalMilliseconds));
	}

	public Task InputTextAsync(string text, CancellationToken cancellationToken = default)
	{
		return Run($"InputText:{text}");
	}

	public Task StartAppAsync(string packageName, CancellationToken cancellationToken = default)
	{
		return Run($"StartApp:{packageName}");
	}

	public Task ForceStopAppAsync(string packageName, CancellationToken cancellationToken = default)
	{
		return Run($"ForceStopApp:{packageName}");
	}

	public Task OpenUriAsync(string uri, CancellationToken cancellationToken = default)
	{
		return Run($"OpenUri:{uri}");
	}

	public Task RebootAsync(
		AndroidRebootMode mode = AndroidRebootMode.Normal,
		CancellationToken cancellationToken = default)
	{
		return Run($"Reboot:{mode}");
	}

	public async Task<byte[]> CaptureScreenshotAsync(CancellationToken cancellationToken = default)
	{
		await Run("CaptureScreenshot");
		return Screenshot;
	}

	public Task<AndroidDeviceStatus?> GetStatusAsync(CancellationToken cancellationToken = default)
	{
		Owner?.Record($"GetStatus:{Serial}");
		return Task.FromResult(Status);
	}

	private Task Run(string call)
	{
		var separator = call.IndexOf(':');
		Owner?.Record(separator < 0 ? $"{call}:{Serial}" : $"{call[..separator]}:{Serial}{call[separator..]}");
		return NextFailure is { } failure ? Task.FromException(failure) : Task.CompletedTask;
	}

	private static string Invariant(int value)
	{
		return value.ToString(CultureInfo.InvariantCulture);
	}
}
