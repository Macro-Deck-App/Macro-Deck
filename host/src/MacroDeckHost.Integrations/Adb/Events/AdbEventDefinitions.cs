using MacroDeck.Localization;
using MacroDeckHost.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;

namespace MacroDeckHost.Integrations.Adb.Events;

internal static class AdbEventDefinitions
{
	private static readonly LocalizedText _category = AppStrings.Integrations.Adb.Events.DevicesCategory();

	public static IReadOnlyList<EventDefinition> All { get; } =
	[
		new()
		{
			Id = AdbEventIds.DeviceConnected,
			Name = AppStrings.Integrations.Adb.Events.DeviceConnectedName(),
			Description = AppStrings.Integrations.Adb.Events.DeviceConnectedDescription(),
			Category = _category,
			ConfigurationParameters = [SerialFilter()],
			PayloadParameters =
			[
				SerialPayload(),
				ActionParameter.Text("model", AppStrings.Integrations.Adb.Params.ModelLabel()),
				ActionParameter.Text("manufacturer", AppStrings.Integrations.Adb.Params.ManufacturerLabel()),
				ActionParameter.Text("state", AppStrings.Integrations.Adb.Params.StateLabel())
			]
		},
		new()
		{
			Id = AdbEventIds.DeviceDisconnected,
			Name = AppStrings.Integrations.Adb.Events.DeviceDisconnectedName(),
			Description = AppStrings.Integrations.Adb.Events.DeviceDisconnectedDescription(),
			Category = _category,
			ConfigurationParameters = [SerialFilter()],
			PayloadParameters = SerialAndModelPayload()
		},
		new()
		{
			Id = AdbEventIds.DeviceAuthorized,
			Name = AppStrings.Integrations.Adb.Events.DeviceAuthorizedName(),
			Description = AppStrings.Integrations.Adb.Events.DeviceAuthorizedDescription(),
			Category = _category,
			ConfigurationParameters = [SerialFilter()],
			PayloadParameters = SerialAndModelPayload()
		},
		new()
		{
			Id = AdbEventIds.DeviceUnauthorized,
			Name = AppStrings.Integrations.Adb.Events.DeviceUnauthorizedName(),
			Description = AppStrings.Integrations.Adb.Events.DeviceUnauthorizedDescription(),
			Category = _category,
			ConfigurationParameters = [SerialFilter()],
			PayloadParameters = SerialAndModelPayload()
		},
		new()
		{
			Id = AdbEventIds.DeviceOnline,
			Name = AppStrings.Integrations.Adb.Events.DeviceOnlineName(),
			Description = AppStrings.Integrations.Adb.Events.DeviceOnlineDescription(),
			Category = _category,
			ConfigurationParameters = [SerialFilter()],
			PayloadParameters = SerialAndModelPayload()
		},
		new()
		{
			Id = AdbEventIds.DeviceOffline,
			Name = AppStrings.Integrations.Adb.Events.DeviceOfflineName(),
			Description = AppStrings.Integrations.Adb.Events.DeviceOfflineDescription(),
			Category = _category,
			ConfigurationParameters = [SerialFilter()],
			PayloadParameters = SerialAndModelPayload()
		}
	];

	private static ActionParameter SerialPayload()
	{
		return ActionParameter.DynamicChoice("serial",
			AppStrings.Integrations.Adb.Params.SerialLabel(),
			optionsSourceId: HostOptionsSources.AndroidDevices);
	}

	private static ActionParameter SerialFilter()
	{
		return ActionParameter.DynamicChoice("serial",
			AppStrings.Integrations.Adb.Params.DeviceLabel(),
			AppStrings.Integrations.Adb.Events.SerialFilterDescription(),
			HostOptionsSources.AndroidDevices,
			AppStrings.Integrations.Adb.Events.SerialFilterPlaceholder(),
			false);
	}

	private static IReadOnlyList<ActionParameter> SerialAndModelPayload()
	{
		return
		[
			SerialPayload(),
			ActionParameter.Text("model", AppStrings.Integrations.Adb.Params.ModelLabel())
		];
	}
}
