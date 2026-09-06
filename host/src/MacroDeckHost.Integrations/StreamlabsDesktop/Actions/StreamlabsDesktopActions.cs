using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.StreamlabsDesktop.Actions;

internal static class StreamlabsDesktopActions
{
	public static IReadOnlyList<IActionDefinition> Create(
		Func<StreamlabsDesktopConnection?> resolver,
		VariableApiAccessor variables) =>
	[
		new SetSceneActionDefinition(resolver),

		new StreamlabsAction("start-streaming",
			AppStrings.Integrations.StreamlabsDesktop.Actions.StartStreamingName(),
			AppStrings.Integrations.StreamlabsDesktop.Actions.StartStreamingDescription(),
			resolver,
			c => c.StartStreamingAsync()),
		new StreamlabsAction("stop-streaming",
			AppStrings.Integrations.StreamlabsDesktop.Actions.StopStreamingName(),
			AppStrings.Integrations.StreamlabsDesktop.Actions.StopStreamingDescription(),
			resolver,
			c => c.StopStreamingAsync()),
		new StreamlabsStateAction("toggle-streaming",
			AppStrings.Integrations.StreamlabsDesktop.Actions.ToggleStreamingName(),
			AppStrings.Integrations.StreamlabsDesktop.Actions.ToggleStreamingDescription(),
			resolver,
			c => c.ToggleStreamingAsync(),
			ActionStates.Streaming,
			state => StateId(ActionStates.Streaming, state.IsStreaming)),

		new StreamlabsAction("start-recording",
			AppStrings.Integrations.StreamlabsDesktop.Actions.StartRecordingName(),
			AppStrings.Integrations.StreamlabsDesktop.Actions.StartRecordingDescription(),
			resolver,
			c => c.StartRecordingAsync()),
		new StreamlabsAction("stop-recording",
			AppStrings.Integrations.StreamlabsDesktop.Actions.StopRecordingName(),
			AppStrings.Integrations.StreamlabsDesktop.Actions.StopRecordingDescription(),
			resolver,
			c => c.StopRecordingAsync()),
		new StreamlabsStateAction("toggle-recording",
			AppStrings.Integrations.StreamlabsDesktop.Actions.ToggleRecordingName(),
			AppStrings.Integrations.StreamlabsDesktop.Actions.ToggleRecordingDescription(),
			resolver,
			c => c.ToggleRecordingAsync(),
			ActionStates.Recording,
			state => StateId(ActionStates.Recording, state.IsRecording)),

		new StreamlabsAction("start-replay-buffer",
			AppStrings.Integrations.StreamlabsDesktop.Actions.StartReplayBufferName(),
			AppStrings.Integrations.StreamlabsDesktop.Actions.StartReplayBufferDescription(),
			resolver,
			c => c.StartReplayBufferAsync()),
		new StreamlabsAction("stop-replay-buffer",
			AppStrings.Integrations.StreamlabsDesktop.Actions.StopReplayBufferName(),
			AppStrings.Integrations.StreamlabsDesktop.Actions.StopReplayBufferDescription(),
			resolver,
			c => c.StopReplayBufferAsync()),
		new StreamlabsStateAction("toggle-replay-buffer",
			AppStrings.Integrations.StreamlabsDesktop.Actions.ToggleReplayBufferName(),
			AppStrings.Integrations.StreamlabsDesktop.Actions.ToggleReplayBufferDescription(),
			resolver,
			c => c.ToggleReplayBufferAsync(),
			ActionStates.OnOff,
			state => StateId(ActionStates.OnOff, state.ReplayBufferActive)),
		new StreamlabsAction("save-replay",
			AppStrings.Integrations.StreamlabsDesktop.Actions.SaveReplayName(),
			AppStrings.Integrations.StreamlabsDesktop.Actions.SaveReplayDescription(),
			resolver,
			c => c.SaveReplayAsync()),

		new SourceVisibilityActionDefinition(resolver),
		new GetSourceVisibilityActionDefinition(resolver, variables),
		new MuteAudioSourceActionDefinition(resolver),
		new SetAudioVolumeActionDefinition(resolver),
		new GetAudioStateActionDefinition(resolver, variables),
		new SceneItemCommandActionDefinition(resolver),

		new StreamlabsStateAction("toggle-studio-mode",
			AppStrings.Integrations.StreamlabsDesktop.Actions.ToggleStudioModeName(),
			AppStrings.Integrations.StreamlabsDesktop.Actions.ToggleStudioModeDescription(),
			resolver,
			c => c.ToggleStudioModeAsync(),
			ActionStates.OnOff,
			state => StateId(ActionStates.OnOff, state.StudioModeActive)),
		new StreamlabsAction("studio-mode-transition",
			AppStrings.Integrations.StreamlabsDesktop.Actions.StudioModeTransitionName(),
			AppStrings.Integrations.StreamlabsDesktop.Actions.StudioModeTransitionDescription(),
			resolver,
			c => c.ExecuteStudioModeTransitionAsync())
	];

	private static string StateId(IReadOnlyList<ActionStateDefinition> states, bool active)
		=> active ? states[1].Id : states[0].Id;
}
