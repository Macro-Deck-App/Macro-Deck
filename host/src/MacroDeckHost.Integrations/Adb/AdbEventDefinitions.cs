using MacroDeck.Localization;
using MacroDeckHost.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;

namespace MacroDeckHost.Integrations.Adb;

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
				ActionParameter.Text("model", label: AppStrings.Integrations.Adb.Params.ModelLabel()),
				ActionParameter.Text("manufacturer", label: AppStrings.Integrations.Adb.Params.ManufacturerLabel()),
				ActionParameter.Text("state", label: AppStrings.Integrations.Adb.Params.StateLabel())
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
		=> ActionParameter.DynamicChoice("serial",
			label: AppStrings.Integrations.Adb.Params.SerialLabel(),
			optionsSourceId: AdbOptionsSourceIds.Devices);

	private static ActionParameter SerialFilter()
		=> ActionParameter.DynamicChoice("serial",
			label: AppStrings.Integrations.Adb.Params.DeviceLabel(),
			description: AppStrings.Integrations.Adb.Events.SerialFilterDescription(),
			optionsSourceId: AdbOptionsSourceIds.Devices,
			placeholder: AppStrings.Integrations.Adb.Events.SerialFilterPlaceholder(),
			required: false);

	private static IReadOnlyList<ActionParameter> SerialAndModelPayload()
		=>
		[
			SerialPayload(),
			ActionParameter.Text("model", label: AppStrings.Integrations.Adb.Params.ModelLabel())
		];
}
