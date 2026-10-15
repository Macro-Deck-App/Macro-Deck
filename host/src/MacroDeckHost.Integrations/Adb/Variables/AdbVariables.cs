using System.Text;
using MacroDeckHost.Integrations.Adb.Connections;
using MacroDeckHost.Localization;
using MacroDeck.Sdk.Android;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Integrations.Adb.Variables;

internal static class AdbVariables
{
	public const string ConnectedDeviceCount = "adb_connected_device_count";
	public const string AuthorizedDeviceCount = "adb_authorized_device_count";
	public const string DeviceConnected = "adb_device_connected";
	public const string DeviceAuthorized = "adb_device_authorized";
	public const string DeviceModel = "adb_device_model";
	public const string DeviceManufacturer = "adb_device_manufacturer";
	public const string DeviceBatteryLevel = "adb_device_battery_level";
	public const string DeviceScreenOn = "adb_device_screen_on";
	public const string DeviceLocked = "adb_device_locked";
	public const string DeviceForegroundApp = "adb_device_foreground_app";

	private static readonly TimeSpan _fast = TimeSpan.FromSeconds(5);
	private static readonly TimeSpan _medium = TimeSpan.FromSeconds(10);
	private static readonly TimeSpan _slow = TimeSpan.FromSeconds(15);
	private static readonly TimeSpan _slower = TimeSpan.FromSeconds(30);
	private static readonly TimeSpan _slowest = TimeSpan.FromSeconds(60);

	public static IReadOnlyList<VariableDefinition> All { get; } =
	[
		VariableDefinition.Eager(ConnectedDeviceCount, VariableType.Numeric, 0, _fast)
			with
			{
				DisplayName = AppStrings.Integrations.Adb.Variables.ConnectedDeviceCount()
			},
		VariableDefinition.Eager(AuthorizedDeviceCount, VariableType.Numeric, 0, _fast)
			with
			{
				DisplayName = AppStrings.Integrations.Adb.Variables.AuthorizedDeviceCount()
			},
		VariableDefinition.Eager(DeviceConnected,
				VariableType.Boolean,
				refreshInterval: _fast)
			with
			{
				DisplayName = AppStrings.Integrations.Adb.Variables.DeviceConnected()
			},
		VariableDefinition.Eager(DeviceAuthorized,
				VariableType.Boolean,
				refreshInterval: _fast)
			with
			{
				DisplayName = AppStrings.Integrations.Adb.Variables.DeviceAuthorized()
			},
		VariableDefinition.Eager(DeviceModel, VariableType.Text, refreshInterval: _slower)
			with
			{
				DisplayName = AppStrings.Integrations.Adb.Variables.DeviceModel()
			},
		VariableDefinition.Eager(DeviceManufacturer,
				VariableType.Text,
				refreshInterval: _slowest)
			with
			{
				DisplayName = AppStrings.Integrations.Adb.Variables.DeviceManufacturer()
			},
		VariableDefinition.Eager(DeviceBatteryLevel, VariableType.Numeric, 0, _slow)
			with
			{
				DisplayName = AppStrings.Integrations.Adb.Variables.DeviceBatteryLevel()
			},
		VariableDefinition.Eager(DeviceScreenOn, VariableType.Boolean, refreshInterval: _slow)
			with
			{
				DisplayName = AppStrings.Integrations.Adb.Variables.DeviceScreenOn()
			},
		VariableDefinition.Eager(DeviceLocked, VariableType.Boolean, refreshInterval: _slow)
			with
			{
				DisplayName = AppStrings.Integrations.Adb.Variables.DeviceLocked()
			},
		VariableDefinition.Eager(DeviceForegroundApp,
				VariableType.Text,
				refreshInterval: _slow)
			with
			{
				DisplayName = AppStrings.Integrations.Adb.Variables.DeviceForegroundApp()
			}
	];

	public static IReadOnlyList<VariableDefinition> DeviceTemplates { get; } = BuildDeviceTemplates();

	public static ValueTask<VariableReading> ReadAsync(
		IAndroidDeviceManager? manager,
		AdbDeviceTracker? tracker,
		string localId,
		CancellationToken cancellationToken)
	{
		if (manager is null || manager.Access != AndroidDeviceAccess.Available)
		{
			return ValueTask.FromResult(VariableReading.Unavailable);
		}

		// A definition id is the canonical name with '_' swapped for '-', and a canonical variable name can
		// never contain '-', so swapping the separator back recovers the name exactly.
		var name = localId.Replace('-', '_');

		if (string.Equals(name, ConnectedDeviceCount, StringComparison.Ordinal))
		{
			return ValueTask.FromResult(VariableReading.Of(manager.Devices.Count));
		}

		if (string.Equals(name, AuthorizedDeviceCount, StringComparison.Ordinal))
		{
			return ValueTask.FromResult(VariableReading.Of(manager.Devices.Count(device =>
				device.State == AndroidDeviceState.Online)));
		}

		if (name is DeviceBatteryLevel
			or DeviceScreenOn
			or DeviceLocked
			or DeviceForegroundApp)
		{
			return ReadPropertyAsync(manager.DefaultDevice, name, cancellationToken);
		}

		var device = ResolveDefaultDevice(manager, tracker);

		object? value = name switch
		{
			DeviceConnected => device is null ? null : device.Status != AdbDeviceStatus.Disconnected,
			DeviceAuthorized => device?.IsAuthorized,
			DeviceModel => device?.Model,
			DeviceManufacturer => device?.Manufacturer,
			_ => null
		};

		return ValueTask.FromResult(VariableReading.Of(value));
	}

	internal static string SanitizeSerial(string serial)
	{
		var builder = new StringBuilder(serial.Length);
		var lastWasUnderscore = false;

		foreach (var character in serial)
		{
			char mapped;
			if (character is >= 'a' and <= 'z' or >= '0' and <= '9')
			{
				mapped = character;
			}
			else if (character is >= 'A' and <= 'Z')
			{
				mapped = char.ToLowerInvariant(character);
			}
			else
			{
				mapped = '_';
			}

			if (mapped == '_' && lastWasUnderscore)
			{
				continue;
			}

			builder.Append(mapped);
			lastWasUnderscore = mapped == '_';
		}

		return builder.ToString().Trim('_');
	}

	private static AdbTrackedDevice? ResolveDefaultDevice(IAndroidDeviceManager manager, AdbDeviceTracker? tracker)
	{
		if (manager.DefaultDevice is not { } device)
		{
			return null;
		}

		if (manager.FindDevice(device.Serial) is { } attached)
		{
			return new AdbTrackedDevice(attached,
				AdbDeviceTracker.StatusOf(attached),
				attached.Info.Model,
				attached.Info.Manufacturer);
		}

		return tracker?.Find(device.Serial) is { Status: AdbDeviceStatus.Disconnected } gone ? gone : null;
	}

	private static async ValueTask<VariableReading> ReadPropertyAsync(
		IAndroidDevice? device,
		string name,
		CancellationToken cancellationToken)
	{
		if (device is null)
		{
			return VariableReading.Unavailable;
		}

		try
		{
			if (name == DeviceBatteryLevel)
			{
				return VariableReading.Of((await device.GetBatteryStateAsync(cancellationToken)).Level);
			}

			var status = await device.GetStatusAsync(cancellationToken);
			if (status is null)
			{
				return VariableReading.Unavailable;
			}

			return VariableReading.Of(name switch
			{
				DeviceScreenOn => status.IsScreenOn,
				DeviceLocked => status.IsLocked,
				DeviceForegroundApp => status.ForegroundPackage,
				_ => null
			});
		}
		catch (AndroidDeviceException)
		{
			return VariableReading.Unavailable;
		}
	}

	private static List<VariableDefinition> BuildDeviceTemplates()
	{
		var placeholder = VariableNameTemplate.Placeholder("device");
		return
		[
			VariableDefinition.Eager($"adb_device_{placeholder}_connected", VariableType.Boolean),
			VariableDefinition.Eager($"adb_device_{placeholder}_authorized", VariableType.Boolean),
			VariableDefinition.Eager($"adb_device_{placeholder}_model", VariableType.Text),
			VariableDefinition.Eager($"adb_device_{placeholder}_battery_level", VariableType.Numeric, 0)
		];
	}
}
