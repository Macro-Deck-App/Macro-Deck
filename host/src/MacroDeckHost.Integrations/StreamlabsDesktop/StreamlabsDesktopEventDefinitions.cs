using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.StreamlabsDesktop;

internal static class StreamlabsDesktopEventDefinitions
{
	public static IReadOnlyList<EventDefinition> All { get; } =
	[
		new()
		{
			Id = StreamlabsDesktopEventIds.SceneChanged,
			Name = AppStrings.Integrations.StreamlabsDesktop.Events.SceneChangedName(),
			Description = AppStrings.Integrations.StreamlabsDesktop.Events.SceneChangedDescription(),
			Category = AppStrings.Integrations.StreamlabsDesktop.Events.ScenesCategory(),
			ConfigurationParameters =
			[
				ActionParameter.DynamicChoice("sceneName",
					label: AppStrings.Integrations.StreamlabsDesktop.Params.SceneLabel(),
					description: AppStrings.Integrations.StreamlabsDesktop.Events
						.OnlyRunWhenSwitchingToThisSceneDescription(),
					placeholder: AppStrings.Integrations.StreamlabsDesktop.Events.AnyScenePlaceholder())
			],
			PayloadParameters =
			[
				ActionParameter.DynamicChoice("sceneName",
					label: AppStrings.Integrations.StreamlabsDesktop.Params.SceneLabel()),
				ActionParameter.DynamicChoice("previousSceneName",
					label: AppStrings.Integrations.StreamlabsDesktop.Events.PreviousSceneLabel())
			]
		},
		Simple(StreamlabsDesktopEventIds.StreamingStarted,
			AppStrings.Integrations.StreamlabsDesktop.Events.StreamingStartedName(),
			AppStrings.Integrations.StreamlabsDesktop.Events.StreamingCategory()),
		Simple(StreamlabsDesktopEventIds.StreamingStopped,
			AppStrings.Integrations.StreamlabsDesktop.Events.StreamingStoppedName(),
			AppStrings.Integrations.StreamlabsDesktop.Events.StreamingCategory()),
		Status(StreamlabsDesktopEventIds.StreamingStatusChanged,
			AppStrings.Integrations.StreamlabsDesktop.Events.StreamingStatusChangedName(),
			AppStrings.Integrations.StreamlabsDesktop.Events.StreamingCategory(),
			AppStrings.Integrations.StreamlabsDesktop.Events.StreamingStatusChangedDescription()),
		Simple(StreamlabsDesktopEventIds.RecordingStarted,
			AppStrings.Integrations.StreamlabsDesktop.Events.RecordingStartedName(),
			AppStrings.Integrations.StreamlabsDesktop.Events.RecordingCategory()),
		Simple(StreamlabsDesktopEventIds.RecordingStopped,
			AppStrings.Integrations.StreamlabsDesktop.Events.RecordingStoppedName(),
			AppStrings.Integrations.StreamlabsDesktop.Events.RecordingCategory()),
		Status(StreamlabsDesktopEventIds.RecordingStatusChanged,
			AppStrings.Integrations.StreamlabsDesktop.Events.RecordingStatusChangedName(),
			AppStrings.Integrations.StreamlabsDesktop.Events.RecordingCategory(),
			AppStrings.Integrations.StreamlabsDesktop.Events.RecordingStatusChangedDescription()),
		Simple(StreamlabsDesktopEventIds.ReplayBufferStarted,
			AppStrings.Integrations.StreamlabsDesktop.Events.ReplayBufferStartedName(),
			AppStrings.Integrations.StreamlabsDesktop.Events.ReplayBufferCategory()),
		Simple(StreamlabsDesktopEventIds.ReplayBufferStopped,
			AppStrings.Integrations.StreamlabsDesktop.Events.ReplayBufferStoppedName(),
			AppStrings.Integrations.StreamlabsDesktop.Events.ReplayBufferCategory()),
		new()
		{
			Id = StreamlabsDesktopEventIds.StudioModeChanged,
			Name = AppStrings.Integrations.StreamlabsDesktop.Events.StudioModeChangedName(),
			Category = AppStrings.Integrations.StreamlabsDesktop.Events.StudioModeCategory(),
			PayloadParameters = [ActionParameter.Toggle("enabled", label: MacroDeckStrings.Common.Enabled())]
		},
		new()
		{
			Id = StreamlabsDesktopEventIds.SourceMuteChanged,
			Name = AppStrings.Integrations.StreamlabsDesktop.Events.SourceMuteChangedName(),
			Description = AppStrings.Integrations.StreamlabsDesktop.Events.SourceMuteChangedDescription(),
			Category = AppStrings.Integrations.StreamlabsDesktop.Events.AudioCategory(),
			ConfigurationParameters =
			[
				ActionParameter.DynamicChoice("sourceName",
					label: AppStrings.Integrations.StreamlabsDesktop.Params.AudioSourceLabel(),
					description: AppStrings.Integrations.StreamlabsDesktop.Events.OnlyRunForThisSourceDescription(),
					placeholder: AppStrings.Integrations.StreamlabsDesktop.Events.AnyAudioSourcePlaceholder())
			],
			PayloadParameters =
			[
				ActionParameter.DynamicChoice("sourceName",
					label: AppStrings.Integrations.StreamlabsDesktop.Params.AudioSourceLabel()),
				ActionParameter.Toggle("muted", label: AppStrings.Integrations.StreamlabsDesktop.Params.MutedLabel())
			]
		},
		new()
		{
			Id = StreamlabsDesktopEventIds.SourceVisibilityChanged,
			Name = AppStrings.Integrations.StreamlabsDesktop.Events.SourceVisibilityChangedName(),
			Description = AppStrings.Integrations.StreamlabsDesktop.Events.SourceVisibilityChangedDescription(),
			Category = AppStrings.Integrations.StreamlabsDesktop.Events.SourcesCategory(),
			ConfigurationParameters =
			[
				ActionParameter.DynamicChoice("sceneName",
					label: AppStrings.Integrations.StreamlabsDesktop.Params.SceneLabel(),
					description: AppStrings.Integrations.StreamlabsDesktop.Events.OnlyRunForThisSceneDescription(),
					placeholder: AppStrings.Integrations.StreamlabsDesktop.Events.AnyScenePlaceholder()),
				ActionParameter.DynamicChoice("sourceName",
					label: AppStrings.Integrations.StreamlabsDesktop.Params.SourceLabel(),
					description: AppStrings.Integrations.StreamlabsDesktop.Events.OnlyRunForThisSourceDescription(),
					placeholder: AppStrings.Integrations.StreamlabsDesktop.Events.AnySourcePlaceholder())
			],
			PayloadParameters =
			[
				ActionParameter.DynamicChoice("sceneName",
					label: AppStrings.Integrations.StreamlabsDesktop.Params.SceneLabel()),
				ActionParameter.Text("sourceName",
					label: AppStrings.Integrations.StreamlabsDesktop.Params.SourceLabel()),
				ActionParameter.Toggle("visible",
					label: AppStrings.Integrations.StreamlabsDesktop.Events.VisibleLabel())
			]
		},
		Simple(StreamlabsDesktopEventIds.Connected,
			MacroDeckStrings.Connection.Connected(),
			AppStrings.Integrations.StreamlabsDesktop.Events.ConnectionCategory()),
		Simple(StreamlabsDesktopEventIds.Disconnected,
			MacroDeckStrings.Connection.Disconnected(),
			AppStrings.Integrations.StreamlabsDesktop.Events.ConnectionCategory())
	];

	private static EventDefinition Simple(string id, LocalizedText name, LocalizedText category) => new()
	{
		Id = id,
		Name = name,
		Category = category
	};

	private static EventDefinition Status(string id,
		LocalizedText name,
		LocalizedText category,
		LocalizedText description) => new()
	{
		Id = id,
		Name = name,
		Description = description,
		Category = category,
		PayloadParameters =
		[
			ActionParameter.Text("status", label: AppStrings.Integrations.StreamlabsDesktop.Events.StatusLabel()),
			ActionParameter.Text("previousStatus",
				label: AppStrings.Integrations.StreamlabsDesktop.Events.PreviousStatusLabel())
		]
	};
}
