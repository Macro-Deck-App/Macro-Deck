using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Streamerbot.Events;

internal static class StreamerbotEventDefinitions
{
	public static IReadOnlyList<EventDefinition> All { get; } =
	[
		new()
		{
			Id = StreamerbotEventIds.Event,
			Name = AppStrings.Integrations.Streamerbot.Events.EventName(),
			Description = AppStrings.Integrations.Streamerbot.Events.EventDescription(),
			Category = AppStrings.Integrations.Streamerbot.Events.EventsCategory(),
			ConfigurationParameters =
			[
				ActionParameter.DynamicChoice("source",
					AppStrings.Integrations.Streamerbot.Events.SourceLabel(),
					AppStrings.Integrations.Streamerbot.Events.SourceDescription(),
					placeholder: AppStrings.Integrations.Streamerbot.Events.AnySourcePlaceholder()),
				ActionParameter.DynamicChoice("type",
					AppStrings.Integrations.Streamerbot.Events.EventTypeLabel(),
					AppStrings.Integrations.Streamerbot.Events.EventTypeDescription(),
					placeholder: AppStrings.Integrations.Streamerbot.Events.AnyEventPlaceholder())
			],
			PayloadParameters =
			[
				ActionParameter.DynamicChoice("source",
					AppStrings.Integrations.Streamerbot.Events.SourceLabel()),
				ActionParameter.DynamicChoice("type",
					AppStrings.Integrations.Streamerbot.Events.EventTypeLabel()),
				ActionParameter.Text("user", AppStrings.Integrations.Streamerbot.Events.UserLabel()),
				ActionParameter.Text("message", AppStrings.Integrations.Streamerbot.Events.MessageLabel()),
				ActionParameter.Text("data", AppStrings.Integrations.Streamerbot.Events.RawDataLabel())
			]
		},
		new()
		{
			Id = StreamerbotEventIds.Connected,
			Name = MacroDeckStrings.Connection.Connected(),
			Description = AppStrings.Integrations.Streamerbot.Events.ConnectedDescription(),
			Category = AppStrings.Integrations.Streamerbot.Events.ConnectionCategory()
		},
		new()
		{
			Id = StreamerbotEventIds.Disconnected,
			Name = MacroDeckStrings.Connection.Disconnected(),
			Description = AppStrings.Integrations.Streamerbot.Events.DisconnectedDescription(),
			Category = AppStrings.Integrations.Streamerbot.Events.ConnectionCategory()
		}
	];
}
