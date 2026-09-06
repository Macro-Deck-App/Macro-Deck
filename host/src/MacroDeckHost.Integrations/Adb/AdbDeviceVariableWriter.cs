using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using Serilog;

namespace MacroDeckHost.Integrations.Adb;

internal static class AdbDeviceVariableWriter
{
	private static readonly ILogger _logger =
		IntegrationLog.For(AdbIntegration.IntegrationId).ForContext(typeof(AdbDeviceVariableWriter));

	public static async Task HandleAsync(
		IVariableApi variables,
		IAdbGateway gateway,
		AdbGatewayDeviceChange change,
		CancellationToken cancellationToken)
	{
		var key = DeviceKey(gateway, change.Device);
		if (key.Length == 0)
		{
			return;
		}

		try
		{
			if (change.Kind == AdbGatewayDeviceChangeKind.Disconnected)
			{
				await ClearAsync(variables, key);
				return;
			}

			var connectedName = NameFor(key, "connected");

			if (change.Kind != AdbGatewayDeviceChangeKind.Authorized &&
				await variables.GetByNameAsync(connectedName) is null)
			{
				return;
			}

			var properties = await gateway.GetPropertiesAsync(change.Device.Serial, cancellationToken);

			await UpsertAsync(variables,
				connectedName,
				VariableType.Boolean,
				change.Device.State != AdbGatewayDeviceState.Disconnected,
				null);
			await UpsertAsync(variables,
				NameFor(key, "authorized"),
				VariableType.Boolean,
				change.Device.IsAuthorized,
				null);
			await UpsertAsync(variables,
				NameFor(key, "model"),
				VariableType.Text,
				change.Device.Model,
				null);
			await UpsertAsync(variables,
				NameFor(key, "battery_level"),
				VariableType.Numeric,
				properties?.BatteryLevel,
				0);
		}
		catch (Exception ex)
		{
			// CreateAsync throws on a validation or collision error, per its own doc comment; a push
			// that fails here must not take the DeviceChanged raise down with it.
			_logger.Warning(ex, "Could not update per-device variables for {Serial}", change.Device.Serial);
		}
	}

	internal static string DeviceKey(IAdbGateway gateway, AdbGatewayDevice device)
	{
		var name = BaseKey(device);
		var sameName = gateway.Devices
			.Where(candidate =>
				string.Equals(BaseKey(candidate), name, StringComparison.Ordinal))
			.Select(candidate => candidate.Serial)
			.OrderBy(serial => serial, StringComparer.Ordinal)
			.ToList();

		var index = sameName.IndexOf(device.Serial);
		return index <= 0 ? name : $"{name}_{index + 1}";
	}

	// Falls back to a generic stem rather than the serial: a device that reports no model must not be
	// the one case that puts a serial back into a variable name.
	private static string BaseKey(AdbGatewayDevice device)
	{
		var model = AdbVariables.SanitizeSerial(device.Model ?? string.Empty).Trim('_');
		return model.Length == 0 ? "unknown" : model;
	}

	private static string NameFor(string key, string suffix) => $"adb_device_{key}_{suffix}";

	private static async Task ClearAsync(IVariableApi variables, string key)
	{
		await ClearIfPresentAsync(variables, NameFor(key, "connected"));
		await ClearIfPresentAsync(variables,
			NameFor(key, "authorized"));
		await ClearIfPresentAsync(variables, NameFor(key, "model"));
		await ClearIfPresentAsync(variables,
			NameFor(key, "battery_level"));
	}

	private static async Task ClearIfPresentAsync(IVariableApi variables, string name)
	{
		var handle = await variables.GetByNameAsync(name);
		if (handle is not null)
		{
			await variables.SetValueAsync(handle.Id, null);
		}
	}

	private static async Task UpsertAsync(
		IVariableApi variables,
		string name,
		VariableType type,
		object? value,
		int? decimalPlaces)
	{
		var handle = await variables.GetByNameAsync(name) ??
			await variables.CreateAsync(name, type, value, decimalPlaces);
		await variables.SetValueAsync(handle.Id, value);
	}
}
