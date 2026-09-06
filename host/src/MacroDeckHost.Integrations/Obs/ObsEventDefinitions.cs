using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using MacroDeckHost.Localization;
using MacroDeckHost.Integrations.Obs.Actions;

namespace MacroDeckHost.Integrations.Obs;

internal static class ObsEventDefinitions
{
	public static IReadOnlyList<EventDefinition> All { get; } =
	[
		SceneEvent(ObsEventIds.SceneChanged,
			AppStrings.Integrations.Obs.Events.SceneChangedName(),
			AppStrings.Integrations.Obs.Events.SceneChangedDescription()),
		SceneEvent(ObsEventIds.PreviewSceneChanged,
			AppStrings.Integrations.Obs.Events.PreviewSceneChangedName(),
			AppStrings.Integrations.Obs.Events.PreviewSceneChangedDescription()),
		Simple(ObsEventIds.RecordingStarted,
			AppStrings.Integrations.Obs.Events.RecordingStartedName(),
			AppStrings.Integrations.Obs.Events.RecordingCategory()),
		Simple(ObsEventIds.RecordingStopped,
			AppStrings.Integrations.Obs.Events.RecordingStoppedName(),
			AppStrings.Integrations.Obs.Events.RecordingCategory()),
		Simple(ObsEventIds.RecordingPaused,
			AppStrings.Integrations.Obs.Events.RecordingPausedName(),
			AppStrings.Integrations.Obs.Events.RecordingCategory()),
		Simple(ObsEventIds.RecordingResumed,
			AppStrings.Integrations.Obs.Events.RecordingResumedName(),
			AppStrings.Integrations.Obs.Events.RecordingCategory()),
		Simple(ObsEventIds.StreamingStarted,
			AppStrings.Integrations.Obs.Events.StreamingStartedName(),
			AppStrings.Integrations.Obs.Events.StreamingCategory()),
		Simple(ObsEventIds.StreamingStopped,
			AppStrings.Integrations.Obs.Events.StreamingStoppedName(),
			AppStrings.Integrations.Obs.Events.StreamingCategory()),
		Simple(ObsEventIds.ReplayBufferStarted,
			AppStrings.Integrations.Obs.Events.ReplayBufferStartedName(),
			AppStrings.Integrations.Obs.Events.ReplayBufferCategory()),
		Simple(ObsEventIds.ReplayBufferStopped,
			AppStrings.Integrations.Obs.Events.ReplayBufferStoppedName(),
			AppStrings.Integrations.Obs.Events.ReplayBufferCategory()),
		new()
		{
			Id = ObsEventIds.ReplayBufferSaved,
			Name = AppStrings.Integrations.Obs.Events.ReplayBufferSavedName(),
			Description = AppStrings.Integrations.Obs.Events.ReplayBufferSavedDescription(),
			Category = AppStrings.Integrations.Obs.Events.ReplayBufferCategory(),
			ConfigurationParameters = [ConfigurationParameter()],
			PayloadParameters =
			[
				ConfigurationPayload(),
				ActionParameter.Text("path", label: AppStrings.Integrations.Obs.Events.PathLabel())
			]
		},
		Simple(ObsEventIds.VirtualCamStarted,
			AppStrings.Integrations.Obs.Events.VirtualCamStartedName(),
			AppStrings.Integrations.Obs.Events.VirtualCameraCategory()),
		Simple(ObsEventIds.VirtualCamStopped,
			AppStrings.Integrations.Obs.Events.VirtualCamStoppedName(),
			AppStrings.Integrations.Obs.Events.VirtualCameraCategory()),
		new()
		{
			Id = ObsEventIds.StudioModeChanged,
			Name = AppStrings.Integrations.Obs.Events.StudioModeChangedName(),
			Category = AppStrings.Integrations.Obs.Events.StudioModeCategory(),
			ConfigurationParameters = [ConfigurationParameter()],
			PayloadParameters =
			[
				ConfigurationPayload(),
				ActionParameter.Toggle("enabled", label: MacroDeckStrings.Common.Enabled())
			]
		},
		new()
		{
			Id = ObsEventIds.InputMuteChanged,
			Name = AppStrings.Integrations.Obs.Events.InputMuteChangedName(),
			Description = AppStrings.Integrations.Obs.Events.InputMuteChangedDescription(),
			Category = AppStrings.Integrations.Obs.Events.AudioCategory(),
			ConfigurationParameters =
			[
				ConfigurationParameter(),
				ActionParameter.DynamicChoice("inputName",
					label: AppStrings.Integrations.Obs.Params.Input(),
					description: AppStrings.Integrations.Obs.Events.InputMuteConfigDescription())
			],
			PayloadParameters =
			[
				ConfigurationPayload(),
				ActionParameter.DynamicChoice("inputName", label: AppStrings.Integrations.Obs.Params.Input()),
				ActionParameter.Toggle("muted", label: AppStrings.Integrations.Obs.Events.MutedLabel())
			]
		},
		Simple(ObsEventIds.Connected,
			MacroDeckStrings.Connection.Connected(),
			AppStrings.Integrations.Obs.Events.ConnectionCategory()),
		Simple(ObsEventIds.Disconnected,
			MacroDeckStrings.Connection.Disconnected(),
			AppStrings.Integrations.Obs.Events.ConnectionCategory())
	];

	private static EventDefinition SceneEvent(string id, LocalizedText name, LocalizedText description) => new()
	{
		Id = id,
		Name = name,
		Description = description,
		Category = AppStrings.Integrations.Obs.Events.ScenesCategory(),
		ConfigurationParameters =
		[
			ConfigurationParameter(),
			ActionParameter.DynamicChoice("sceneName",
				label: AppStrings.Integrations.Obs.Params.Scene(),
				description: AppStrings.Integrations.Obs.Events.SceneConfigDescription())
		],
		PayloadParameters =
		[
			ConfigurationPayload(),
			ActionParameter.DynamicChoice("sceneName", label: AppStrings.Integrations.Obs.Params.Scene()),
			ActionParameter.DynamicChoice("previousSceneName",
				label: AppStrings.Integrations.Obs.Events.PreviousSceneLabel())
		]
	};

	private static EventDefinition Simple(string id, LocalizedText name, LocalizedText category) => new()
	{
		Id = id,
		Name = name,
		Category = category,
		ConfigurationParameters = [ConfigurationParameter()],
		PayloadParameters = [ConfigurationPayload()]
	};

	private static ActionParameter ConfigurationParameter()
		=> ActionParameter.DynamicChoice(ObsTargetResolver.ConfigurationParameter,
			label: AppStrings.Integrations.Obs.Params.Configuration(),
			required: true);

	private static ActionParameter ConfigurationPayload()
		=> ActionParameter.DynamicChoice(ObsTargetResolver.ConfigurationParameter,
			label: AppStrings.Integrations.Obs.Params.Configuration(),
			required: true);
}
