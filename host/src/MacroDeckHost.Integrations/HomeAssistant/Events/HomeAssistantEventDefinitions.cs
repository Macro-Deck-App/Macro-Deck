using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.HomeAssistant.Events;

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
					AppStrings.Integrations.HomeAssistant.Events.EntityLabel(),
					AppStrings.Integrations.HomeAssistant.Events.EntityFilterDescription(),
					placeholder: AppStrings.Integrations.HomeAssistant.Events.AnyEntity()),
				ActionParameter.DynamicChoice("domain",
					AppStrings.Integrations.HomeAssistant.Events.DomainLabel(),
					AppStrings.Integrations.HomeAssistant.Events.DomainFilterDescription(),
					placeholder: AppStrings.Integrations.HomeAssistant.Events.AnyDomain()),
				ActionParameter.Autocomplete("toState",
					AppStrings.Integrations.HomeAssistant.Events.NewStateLabel(),
					AppStrings.Integrations.HomeAssistant.Events.ToStateFilterDescription(),
					placeholder: AppStrings.Integrations.HomeAssistant.Events.AnyState()),
				ActionParameter.Autocomplete("fromState",
					AppStrings.Integrations.HomeAssistant.Events.PreviousStateLabel(),
					AppStrings.Integrations.HomeAssistant.Events.FromStateFilterDescription(),
					placeholder: AppStrings.Integrations.HomeAssistant.Events.AnyState())
			],
			PayloadParameters =
			[
				ActionParameter.Autocomplete("entityId",
					AppStrings.Integrations.HomeAssistant.Events.EntityLabel()),
				ActionParameter.DynamicChoice("domain",
					AppStrings.Integrations.HomeAssistant.Events.DomainLabel()),
				ActionParameter.Autocomplete("toState",
					AppStrings.Integrations.HomeAssistant.Events.NewStateLabel()),
				ActionParameter.Autocomplete("fromState",
					AppStrings.Integrations.HomeAssistant.Events.PreviousStateLabel()),
				ActionParameter.Text("friendlyName", AppStrings.Integrations.HomeAssistant.Events.NameLabel()),
				ActionParameter.Text("area", AppStrings.Integrations.HomeAssistant.Events.AreaLabel()),
				ActionParameter.Text("unit", AppStrings.Integrations.HomeAssistant.Events.UnitLabel()),
				ActionParameter.Text("attributes",
					AppStrings.Integrations.HomeAssistant.Events.AttributesJsonLabel())
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
					AppStrings.Integrations.HomeAssistant.Events.EventTypeLabel(),
					AppStrings.Integrations.HomeAssistant.Events.EventTypeFilterDescription(),
					placeholder: AppStrings.Integrations.HomeAssistant.Events.AnyEvent())
			],
			PayloadParameters =
			[
				ActionParameter.Autocomplete("eventType",
					AppStrings.Integrations.HomeAssistant.Events.EventTypeLabel()),
				ActionParameter.Autocomplete("entityId",
					AppStrings.Integrations.HomeAssistant.Events.EntityLabel()),
				ActionParameter.Text("data", AppStrings.Integrations.HomeAssistant.Events.RawDataJsonLabel())
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
