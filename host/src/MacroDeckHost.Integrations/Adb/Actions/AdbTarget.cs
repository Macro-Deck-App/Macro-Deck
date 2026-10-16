using MacroDeck.Sdk.Android;
using MacroDeckHost.Integrations.Adb.Connections;
using MacroDeckHost.Integrations.Adb.Models;

namespace MacroDeckHost.Integrations.Adb.Actions;

// Resolved only when the action runs, so a missing parameter is reported before a missing device.
internal sealed class AdbTarget
{
	private readonly IAndroidDeviceManager _manager;
	private readonly AdbDeviceTracker? _tracker;
	private readonly string? _serial;

	public AdbTarget(IAndroidDeviceManager manager, AdbDeviceTracker? tracker, string? serial)
	{
		_manager = manager;
		_tracker = tracker;
		_serial = serial;
	}

	public async Task<AdbOutcome> RunAsync(Func<IAndroidDevice, Task> operation)
	{
		var (outcome, _) = await RunAsync(async device =>
		{
			await operation(device);
			return true;
		});
		return outcome;
	}

	public async Task<(AdbOutcome Outcome, T? Value)> RunAsync<T>(Func<IAndroidDevice, Task<T>> operation)
	{
		// Checked first: without adb no device is ever seen, which would otherwise read as a missing device.
		if (_manager.IsAdbUnavailable)
		{
			return (AdbOutcome.Fail(AndroidDeviceErrorCode.AdbUnavailable, "The adb executable could not be located."),
				default);
		}

		var device = Resolve();
		if (device is null)
		{
			return (AdbOutcome.Fail(AndroidDeviceErrorCode.DeviceNotFound,
					string.IsNullOrWhiteSpace(_serial)
						? "No default device is configured and no single device is connected."
						: $"No device with serial '{_serial}' is known."),
				default);
		}

		try
		{
			return (AdbOutcome.Ok, await operation(device));
		}
		catch (AndroidDeviceException exception)
		{
			return (AdbOutcome.Fail(exception.ErrorCode, exception.Message), default);
		}
	}

	private IAndroidDevice? Resolve()
	{
		if (string.IsNullOrWhiteSpace(_serial))
		{
			return _manager.DefaultDevice;
		}

		return _manager.FindDevice(_serial) ?? _tracker?.Find(_serial)?.Device;
	}
}
