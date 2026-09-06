using MacroDeck.Localization;
using MacroDeckHost.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;

namespace MacroDeckHost.Integrations.Meld;

internal static class MeldEventDefinitions
{
	public static IReadOnlyList<EventDefinition> All { get; } =
	[
		SceneEvent(MeldEventIds.SceneChanged,
			AppStrings.Integrations.Meld.Events.SceneChangedName(),
			AppStrings.Integrations.Meld.Events.SceneChangedDescription()),
		SceneEvent(MeldEventIds.StagedSceneChanged,
			AppStrings.Integrations.Meld.Events.StagedSceneChangedName(),
			AppStrings.Integrations.Meld.Events.StagedSceneChangedDescription()),
		Simple(MeldEventIds.StreamingStarted,
			AppStrings.Integrations.Meld.Events.StreamingStartedName(),
			AppStrings.Integrations.Meld.Events.StreamingCategory()),
		Simple(MeldEventIds.StreamingStopped,
			AppStrings.Integrations.Meld.Events.StreamingStoppedName(),
			AppStrings.Integrations.Meld.Events.StreamingCategory()),
		Simple(MeldEventIds.RecordingStarted,
			AppStrings.Integrations.Meld.Events.RecordingStartedName(),
			AppStrings.Integrations.Meld.Events.RecordingCategory()),
		Simple(MeldEventIds.RecordingStopped,
			AppStrings.Integrations.Meld.Events.RecordingStoppedName(),
			AppStrings.Integrations.Meld.Events.RecordingCategory()),
		new()
		{
			Id = MeldEventIds.LayerVisibilityChanged,
			Name = AppStrings.Integrations.Meld.Events.LayerVisibilityChangedName(),
			Description = AppStrings.Integrations.Meld.Events.LayerVisibilityChangedDescription(),
			Category = AppStrings.Integrations.Meld.Events.LayersCategory(),
			ConfigurationParameters =
			[
				ActionParameter.DynamicChoice("layerId",
					label: AppStrings.Integrations.Meld.Parameters.Layer(),
					placeholder: AppStrings.Integrations.Meld.Parameters.AnyLayer())
			],
			PayloadParameters =
			[
				ActionParameter.DynamicChoice("layerId", label: AppStrings.Integrations.Meld.Parameters.LayerId()),
				ActionParameter.Text("layerName", label: AppStrings.Integrations.Meld.Parameters.Layer()),
				ActionParameter.DynamicChoice("sceneId", label: AppStrings.Integrations.Meld.Parameters.SceneId()),
				ActionParameter.Text("sceneName", label: AppStrings.Integrations.Meld.Parameters.Scene()),
				ActionParameter.Toggle("visible", label: AppStrings.Integrations.Meld.Parameters.Visible())
			]
		},
		new()
		{
			Id = MeldEventIds.EffectStateChanged,
			Name = AppStrings.Integrations.Meld.Events.EffectStateChangedName(),
			Description = AppStrings.Integrations.Meld.Events.EffectStateChangedDescription(),
			Category = AppStrings.Integrations.Meld.Events.EffectsCategory(),
			ConfigurationParameters =
			[
				ActionParameter.DynamicChoice("effectId",
					label: AppStrings.Integrations.Meld.Parameters.Effect(),
					placeholder: AppStrings.Integrations.Meld.Parameters.AnyEffect())
			],
			PayloadParameters =
			[
				ActionParameter.DynamicChoice("effectId", label: AppStrings.Integrations.Meld.Parameters.EffectId()),
				ActionParameter.Text("effectName", label: AppStrings.Integrations.Meld.Parameters.Effect()),
				ActionParameter.DynamicChoice("layerId", label: AppStrings.Integrations.Meld.Parameters.LayerId()),
				ActionParameter.Text("layerName", label: AppStrings.Integrations.Meld.Parameters.Layer()),
				ActionParameter.DynamicChoice("sceneId", label: AppStrings.Integrations.Meld.Parameters.SceneId()),
				ActionParameter.Toggle("enabled", label: AppStrings.Integrations.Meld.Parameters.Enabled())
			]
		},
		new()
		{
			Id = MeldEventIds.TrackMuteChanged,
			Name = AppStrings.Integrations.Meld.Events.TrackMuteChangedName(),
			Description = AppStrings.Integrations.Meld.Events.TrackMuteChangedDescription(),
			Category = AppStrings.Integrations.Meld.Events.AudioCategory(),
			ConfigurationParameters =
			[
				ActionParameter.DynamicChoice("trackId",
					label: AppStrings.Integrations.Meld.Parameters.Track(),
					placeholder: AppStrings.Integrations.Meld.Parameters.AnyTrack())
			],
			PayloadParameters =
			[
				ActionParameter.DynamicChoice("trackId", label: AppStrings.Integrations.Meld.Parameters.TrackId()),
				ActionParameter.Text("trackName", label: AppStrings.Integrations.Meld.Parameters.Track()),
				ActionParameter.Toggle("muted", label: AppStrings.Integrations.Meld.Parameters.Muted())
			]
		},
		new()
		{
			Id = MeldEventIds.TrackMonitoringChanged,
			Name = AppStrings.Integrations.Meld.Events.TrackMonitoringChangedName(),
			Description = AppStrings.Integrations.Meld.Events.TrackMonitoringChangedDescription(),
			Category = AppStrings.Integrations.Meld.Events.AudioCategory(),
			ConfigurationParameters =
			[
				ActionParameter.DynamicChoice("trackId",
					label: AppStrings.Integrations.Meld.Parameters.Track(),
					placeholder: AppStrings.Integrations.Meld.Parameters.AnyTrack())
			],
			PayloadParameters =
			[
				ActionParameter.DynamicChoice("trackId", label: AppStrings.Integrations.Meld.Parameters.TrackId()),
				ActionParameter.Text("trackName", label: AppStrings.Integrations.Meld.Parameters.Track()),
				ActionParameter.Toggle("monitoring", label: AppStrings.Integrations.Meld.Parameters.Monitoring())
			]
		},
		Simple(MeldEventIds.SessionChanged,
			AppStrings.Integrations.Meld.Events.SessionChangedName(),
			AppStrings.Integrations.Meld.Events.SessionCategory(),
			AppStrings.Integrations.Meld.Events.SessionChangedDescription()),
		Simple(MeldEventIds.Connected,
			MacroDeckStrings.Connection.Connected(),
			AppStrings.Integrations.Meld.Events.ConnectionCategory()),
		Simple(MeldEventIds.Disconnected,
			MacroDeckStrings.Connection.Disconnected(),
			AppStrings.Integrations.Meld.Events.ConnectionCategory())
	];

	private static EventDefinition SceneEvent(string id, LocalizedText name, LocalizedText description) => new()
	{
		Id = id,
		Name = name,
		Description = description,
		Category = AppStrings.Integrations.Meld.Events.ScenesCategory(),
		ConfigurationParameters =
		[
			ActionParameter.DynamicChoice("sceneId",
				label: AppStrings.Integrations.Meld.Parameters.Scene(),
				placeholder: AppStrings.Integrations.Meld.Parameters.AnyScene())
		],
		PayloadParameters =
		[
			ActionParameter.DynamicChoice("sceneId", label: AppStrings.Integrations.Meld.Parameters.SceneId()),
			ActionParameter.Text("sceneName", label: AppStrings.Integrations.Meld.Parameters.Scene()),
			ActionParameter.DynamicChoice("previousSceneId",
				label: AppStrings.Integrations.Meld.Parameters.PreviousSceneId()),
			ActionParameter.Text("previousSceneName", label: AppStrings.Integrations.Meld.Parameters.PreviousScene())
		]
	};

	private static EventDefinition Simple(
		string id,
		LocalizedText name,
		LocalizedText category,
		LocalizedText description = default) => new()
	{
		Id = id,
		Name = name,
		Description = description,
		Category = category
	};
}
