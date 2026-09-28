using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using MacroDeckHost.Application.Actions.Options;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Companion;

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
					label: AppStrings.Events.Common.DeviceLabel(),
					description: AppStrings.Events.Common.DeviceFilterDescription(),
					optionsSourceId: DeckOptionsSourceIds.Devices,
					placeholder: AppStrings.Events.Common.AnyDevicePlaceholder())
			],
			PayloadParameters =
			[
				ActionParameter.DynamicChoice("deviceId",
					label: AppStrings.Events.Core.DeviceIdLabel(),
					optionsSourceId: DeckOptionsSourceIds.Devices),
				ActionParameter.Text("deviceName", label: AppStrings.Events.Common.DeviceLabel())
			]
		}
	];
}
