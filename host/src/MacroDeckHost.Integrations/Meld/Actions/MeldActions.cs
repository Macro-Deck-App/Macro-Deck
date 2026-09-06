using MacroDeckHost.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.Meld.Actions;

internal static class MeldActions
{
	public static IReadOnlyList<IActionDefinition>
		Create(Func<MeldConnection?> resolver, VariableApiAccessor variables) =>
	[
		new MeldSceneActionDefinition("show-scene",
			AppStrings.Integrations.Meld.Actions.ShowScene.Name(),
			AppStrings.Integrations.Meld.Actions.ShowScene.Description(),
			resolver,
			(connection, sceneId, ct) => connection.ShowSceneAsync(sceneId, ct),
			session => session.CurrentSceneId),
		new MeldSceneActionDefinition("stage-scene",
			AppStrings.Integrations.Meld.Actions.StageScene.Name(),
			AppStrings.Integrations.Meld.Actions.StageScene.Description(),
			resolver,
			(connection, sceneId, ct) => connection.StageSceneAsync(sceneId, ct),
			session => session.StagedSceneId),
		new MeldCommandAction("show-staged-scene",
			AppStrings.Integrations.Meld.Actions.ShowStagedScene.Name(),
			AppStrings.Integrations.Meld.Actions.ShowStagedScene.Description(),
			resolver,
			(connection, ct) => connection.ShowStagedSceneAsync(ct)),

		new SetLayerVisibilityActionDefinition(resolver),
		new SetEffectStateActionDefinition(resolver),

		new MeldCommandAction("start-streaming",
			AppStrings.Integrations.Meld.Actions.StartStreaming.Name(),
			AppStrings.Integrations.Meld.Actions.StartStreaming.Description(),
			resolver,
			(connection, ct) => connection.SendCommandAsync(MeldObjects.CommandStartStreaming, ct)),
		new MeldCommandAction("stop-streaming",
			AppStrings.Integrations.Meld.Actions.StopStreaming.Name(),
			AppStrings.Integrations.Meld.Actions.StopStreaming.Description(),
			resolver,
			(connection, ct) => connection.SendCommandAsync(MeldObjects.CommandStopStreaming, ct)),
		new MeldStateCommandAction("toggle-streaming",
			AppStrings.Integrations.Meld.Actions.ToggleStreaming.Name(),
			AppStrings.Integrations.Meld.Actions.ToggleStreaming.Description(),
			resolver,
			(connection, ct) => connection.SendCommandAsync(MeldObjects.CommandToggleStreaming, ct),
			ActionStates.Streaming,
			state => state.IsStreaming),

		new MeldCommandAction("start-recording",
			AppStrings.Integrations.Meld.Actions.StartRecording.Name(),
			AppStrings.Integrations.Meld.Actions.StartRecording.Description(),
			resolver,
			(connection, ct) => connection.SendCommandAsync(MeldObjects.CommandStartRecording, ct)),
		new MeldCommandAction("stop-recording",
			AppStrings.Integrations.Meld.Actions.StopRecording.Name(),
			AppStrings.Integrations.Meld.Actions.StopRecording.Description(),
			resolver,
			(connection, ct) => connection.SendCommandAsync(MeldObjects.CommandStopRecording, ct)),
		new MeldStateCommandAction("toggle-recording",
			AppStrings.Integrations.Meld.Actions.ToggleRecording.Name(),
			AppStrings.Integrations.Meld.Actions.ToggleRecording.Description(),
			resolver,
			(connection, ct) => connection.SendCommandAsync(MeldObjects.CommandToggleRecording, ct),
			ActionStates.Recording,
			state => state.IsRecording),

		new TakeScreenshotActionDefinition(resolver),

		new SetTrackMuteActionDefinition(resolver),
		new SetTrackMonitoringActionDefinition(resolver),
		new SetTrackVolumeActionDefinition(resolver),
		new AdjustTrackVolumeActionDefinition(resolver),

		new GetLayerVisibilityActionDefinition(resolver, variables),
		new GetEffectStateActionDefinition(resolver, variables),
		new GetTrackMuteActionDefinition(resolver, variables),
		new GetTrackVolumeActionDefinition(resolver, variables)
	];
}
