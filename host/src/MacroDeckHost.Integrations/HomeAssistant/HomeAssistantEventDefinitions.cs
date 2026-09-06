using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.HomeAssistant;

internal static class HomeAssistantEventDefinitions
{
	public static IReadOnlyList<EventDefinition> All { get; } =
	[
		new()
		{
			Id = HomeAssistantEventIds.EntityStateChanged,
			Name = AppStrings.Integrations.HomeAssistant.Events.EntityStateChangedName(),
			Description = AppStrings.Integrations.HomeAssistant.Events.EntityStateChangedDescription(),
			Category = AppStrings.Integrations.HomeAssistant.Events.EntitiesCategory(),
			ConfigurationParameters =
			[
				ActionParameter.Autocomplete("entityId",
					label: AppStrings.Integrations.HomeAssistant.Events.EntityLabel(),
					description: AppStrings.Integrations.HomeAssistant.Events.EntityFilterDescription(),
					placeholder: AppStrings.Integrations.HomeAssistant.Events.AnyEntity()),
				ActionParameter.DynamicChoice("domain",
					label: AppStrings.Integrations.HomeAssistant.Events.DomainLabel(),
					description: AppStrings.Integrations.HomeAssistant.Events.DomainFilterDescription(),
					placeholder: AppStrings.Integrations.HomeAssistant.Events.AnyDomain()),
				ActionParameter.Autocomplete("toState",
					label: AppStrings.Integrations.HomeAssistant.Events.NewStateLabel(),
					description: AppStrings.Integrations.HomeAssistant.Events.ToStateFilterDescription(),
					placeholder: AppStrings.Integrations.HomeAssistant.Events.AnyState()),
				ActionParameter.Autocomplete("fromState",
					label: AppStrings.Integrations.HomeAssistant.Events.PreviousStateLabel(),
					description: AppStrings.Integrations.HomeAssistant.Events.FromStateFilterDescription(),
					placeholder: AppStrings.Integrations.HomeAssistant.Events.AnyState())
			],
			PayloadParameters =
			[
				ActionParameter.Autocomplete("entityId",
					label: AppStrings.Integrations.HomeAssistant.Events.EntityLabel()),
				ActionParameter.DynamicChoice("domain",
					label: AppStrings.Integrations.HomeAssistant.Events.DomainLabel()),
				ActionParameter.Autocomplete("toState",
					label: AppStrings.Integrations.HomeAssistant.Events.NewStateLabel()),
				ActionParameter.Autocomplete("fromState",
					label: AppStrings.Integrations.HomeAssistant.Events.PreviousStateLabel()),
				ActionParameter.Text("friendlyName", label: AppStrings.Integrations.HomeAssistant.Events.NameLabel()),
				ActionParameter.Text("area", label: AppStrings.Integrations.HomeAssistant.Events.AreaLabel()),
				ActionParameter.Text("unit", label: AppStrings.Integrations.HomeAssistant.Events.UnitLabel()),
				ActionParameter.Text("attributes",
					label: AppStrings.Integrations.HomeAssistant.Events.AttributesJsonLabel())
			]
		},
		new()
		{
			Id = HomeAssistantEventIds.Event,
			Name = AppStrings.Integrations.HomeAssistant.Events.EventName(),
			Description = AppStrings.Integrations.HomeAssistant.Events.EventDescription(),
			Category = AppStrings.Integrations.HomeAssistant.Events.EventsCategory(),
			ConfigurationParameters =
			[
				ActionParameter.Autocomplete("eventType",
					label: AppStrings.Integrations.HomeAssistant.Events.EventTypeLabel(),
					description: AppStrings.Integrations.HomeAssistant.Events.EventTypeFilterDescription(),
					placeholder: AppStrings.Integrations.HomeAssistant.Events.AnyEvent())
			],
			PayloadParameters =
			[
				ActionParameter.Autocomplete("eventType",
					label: AppStrings.Integrations.HomeAssistant.Events.EventTypeLabel()),
				ActionParameter.Autocomplete("entityId",
					label: AppStrings.Integrations.HomeAssistant.Events.EntityLabel()),
				ActionParameter.Text("data", label: AppStrings.Integrations.HomeAssistant.Events.RawDataJsonLabel())
			]
		},
		new()
		{
			Id = HomeAssistantEventIds.Connected,
			Name = MacroDeckStrings.Connection.Connected(),
			Description = AppStrings.Integrations.HomeAssistant.Events.ConnectedDescription(),
			Category = AppStrings.Integrations.HomeAssistant.Events.ConnectionCategory()
		},
		new()
		{
			Id = HomeAssistantEventIds.Disconnected,
			Name = MacroDeckStrings.Connection.Disconnected(),
			Description = AppStrings.Integrations.HomeAssistant.Events.DisconnectedDescription(),
			Category = AppStrings.Integrations.HomeAssistant.Events.ConnectionCategory()
		}
	];
}
