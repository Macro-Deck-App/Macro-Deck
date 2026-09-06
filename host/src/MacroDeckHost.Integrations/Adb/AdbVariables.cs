using System.Text;
using MacroDeckHost.Localization;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Integrations.Adb;

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
		IAdbGateway? gateway,
		string localId,
		CancellationToken cancellationToken)
	{
		if (gateway is null || !gateway.IsEnabled)
		{
			return ValueTask.FromResult(VariableReading.Unavailable);
		}

		// A definition id is the canonical name with '_' swapped for '-', and a canonical variable name can
		// never contain '-', so swapping the separator back recovers the name exactly.
		var name = localId.Replace('-', '_');

		if (string.Equals(name, ConnectedDeviceCount, StringComparison.Ordinal))
		{
			return ValueTask.FromResult(VariableReading.Of(gateway.Devices.Count(device =>
				device.State != AdbGatewayDeviceState.Disconnected)));
		}

		if (string.Equals(name, AuthorizedDeviceCount, StringComparison.Ordinal))
		{
			return ValueTask.FromResult(VariableReading.Of(gateway.Devices.Count(device => device.IsAuthorized)));
		}

		if (name is DeviceBatteryLevel
			or DeviceScreenOn
			or DeviceLocked
			or DeviceForegroundApp)
		{
			return ReadPropertyAsync(gateway, name, cancellationToken);
		}

		var device = ResolveDefaultDevice(gateway);
		bool? connected = device is not null ? device.State != AdbGatewayDeviceState.Disconnected : null;
		bool? authorized = device is not null ? device.IsAuthorized : null;

		object? value = name switch
		{
			DeviceConnected => connected,
			DeviceAuthorized => authorized,
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

	private static AdbGatewayDevice? ResolveDefaultDevice(IAdbGateway gateway)
	{
		var configured = gateway.DefaultDeviceSerial;
		if (!string.IsNullOrWhiteSpace(configured))
		{
			return gateway.Devices.FirstOrDefault(device =>
				string.Equals(device.Serial, configured, StringComparison.Ordinal));
		}

		var connected = gateway.Devices.Where(device => device.State != AdbGatewayDeviceState.Disconnected).ToList();
		return connected.Count == 1 ? connected[0] : null;
	}

	private static async ValueTask<VariableReading> ReadPropertyAsync(
		IAdbGateway gateway,
		string name,
		CancellationToken cancellationToken)
	{
		var properties = await gateway.GetPropertiesAsync(null, cancellationToken);
		if (properties is null)
		{
			return VariableReading.Unavailable;
		}

		return VariableReading.Of(name switch
		{
			DeviceBatteryLevel => properties.BatteryLevel,
			DeviceScreenOn => properties.ScreenOn,
			DeviceLocked => properties.Locked,
			DeviceForegroundApp => properties.ForegroundPackage,
			_ => null
		});
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
