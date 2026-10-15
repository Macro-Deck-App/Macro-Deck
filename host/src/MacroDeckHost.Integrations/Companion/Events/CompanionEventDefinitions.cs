using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Companion.Events;

internal static class CompanionEventDefinitions
{
	public const string DeviceReady = "device-ready";

	public static IReadOnlyList<EventDefinition> All { get; } =
	[
		new()
		{
			Id = DeviceReady,
			Name = AppStrings.Integrations.Companion.Events.DeviceReadyName(),
			Description = AppStrings.Integrations.Companion.Events.DeviceReadyDescription(),
			Category = AppStrings.Events.Core.ClientsCategory(),
			ConfigurationParameters =
			[
				ActionParameter.DynamicChoice("deviceId",
					AppStrings.Events.Common.DeviceLabel(),
					AppStrings.Events.Common.DeviceFilterDescription(),
					HostOptionsSources.Devices,
					AppStrings.Events.Common.AnyDevicePlaceholder())
			],
			PayloadParameters =
			[
				ActionParameter.DynamicChoice("deviceId",
					AppStrings.Events.Core.DeviceIdLabel(),
					optionsSourceId: HostOptionsSources.Devices),
				ActionParameter.Text("deviceName", AppStrings.Events.Common.DeviceLabel())
			]
		}
	];
}
