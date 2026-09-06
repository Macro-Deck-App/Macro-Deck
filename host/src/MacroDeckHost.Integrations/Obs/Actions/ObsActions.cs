using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Obs.Actions;

internal static class ObsActions
{
	public static IReadOnlyList<IActionDefinition> Create(
		Func<ObsConnection?> resolver,
		VariableApiAccessor variables)
		=> Create(ObsTargetResolver.Legacy(resolver), variables);

	public static IReadOnlyList<IActionDefinition> Create(
		ObsTargetResolver resolver,
		VariableApiAccessor variables) =>
	[
		new SceneActionDefinition("set-scene",
			AppStrings.Integrations.Obs.Actions.SetScene.Name(),
			AppStrings.Integrations.Obs.Actions.SetScene.Description(),
			preview: false,
			resolver),
		new SceneActionDefinition("set-preview-scene",
			AppStrings.Integrations.Obs.Actions.SetPreviewScene.Name(),
			AppStrings.Integrations.Obs.Actions.SetPreviewScene.Description(),
			preview: true,
			resolver),

		new ObsAction("start-recording",
			AppStrings.Integrations.Obs.Actions.StartRecording.Name(),
			AppStrings.Integrations.Obs.Actions.StartRecording.Description(),
			resolver,
			c => c.StartRecordingAsync()),
		new ObsAction("stop-recording",
			AppStrings.Integrations.Obs.Actions.StopRecording.Name(),
			AppStrings.Integrations.Obs.Actions.StopRecording.Description(),
			resolver,
			c => c.StopRecordingAsync()),
		new ObsStateAction("toggle-recording",
			AppStrings.Integrations.Obs.Actions.ToggleRecording.Name(),
			AppStrings.Integrations.Obs.Actions.ToggleRecording.Description(),
			resolver,
			c => c.ToggleRecordingAsync(),
			ActionStates.RecordingWithPause,
			RecordingStateId),
		new ObsStateAction("toggle-pause-recording",
			AppStrings.Integrations.Obs.Actions.TogglePauseRecording.Name(),
			AppStrings.Integrations.Obs.Actions.TogglePauseRecording.Description(),
			resolver,
			c => c.TogglePauseRecordingAsync(),
			ActionStates.RecordingWithPause,
			RecordingStateId),

		new ObsAction("start-streaming",
			AppStrings.Integrations.Obs.Actions.StartStreaming.Name(),
			AppStrings.Integrations.Obs.Actions.StartStreaming.Description(),
			resolver,
			c => c.StartStreamingAsync()),
		new ObsAction("stop-streaming",
			AppStrings.Integrations.Obs.Actions.StopStreaming.Name(),
			AppStrings.Integrations.Obs.Actions.StopStreaming.Description(),
			resolver,
			c => c.StopStreamingAsync()),
		new ObsStateAction("toggle-streaming",
			AppStrings.Integrations.Obs.Actions.ToggleStreaming.Name(),
			AppStrings.Integrations.Obs.Actions.ToggleStreaming.Description(),
			resolver,
			c => c.ToggleStreamingAsync(),
			ActionStates.Streaming,
			state => StateId(ActionStates.Streaming, state.IsStreaming)),

		new ObsAction("start-virtual-camera",
			AppStrings.Integrations.Obs.Actions.StartVirtualCamera.Name(),
			AppStrings.Integrations.Obs.Actions.StartVirtualCamera.Description(),
			resolver,
			c => c.StartVirtualCamAsync()),
		new ObsAction("stop-virtual-camera",
			AppStrings.Integrations.Obs.Actions.StopVirtualCamera.Name(),
			AppStrings.Integrations.Obs.Actions.StopVirtualCamera.Description(),
			resolver,
			c => c.StopVirtualCamAsync()),
		new ObsStateAction("toggle-virtual-camera",
			AppStrings.Integrations.Obs.Actions.ToggleVirtualCamera.Name(),
			AppStrings.Integrations.Obs.Actions.ToggleVirtualCamera.Description(),
			resolver,
			c => c.ToggleVirtualCamAsync(),
			ActionStates.OnOff,
			state => StateId(ActionStates.OnOff, state.VirtualCamActive)),

		new ObsAction("start-replay-buffer",
			AppStrings.Integrations.Obs.Actions.StartReplayBuffer.Name(),
			AppStrings.Integrations.Obs.Actions.StartReplayBuffer.Description(),
			resolver,
			c => c.StartReplayBufferAsync()),
		new ObsAction("stop-replay-buffer",
			AppStrings.Integrations.Obs.Actions.StopReplayBuffer.Name(),
			AppStrings.Integrations.Obs.Actions.StopReplayBuffer.Description(),
			resolver,
			c => c.StopReplayBufferAsync()),
		new ObsStateAction("toggle-replay-buffer",
			AppStrings.Integrations.Obs.Actions.ToggleReplayBuffer.Name(),
			AppStrings.Integrations.Obs.Actions.ToggleReplayBuffer.Description(),
			resolver,
			c => c.ToggleReplayBufferAsync(),
			ActionStates.OnOff,
			state => StateId(ActionStates.OnOff, state.ReplayBufferActive)),
		new ObsAction("save-replay-buffer",
			AppStrings.Integrations.Obs.Actions.SaveReplayBuffer.Name(),
			AppStrings.Integrations.Obs.Actions.SaveReplayBuffer.Description(),
			resolver,
			c => c.SaveReplayBufferAsync()),

		new SourceVisibilityActionDefinition(resolver),
		new MuteInputActionDefinition(resolver),
		new SetInputVolumeActionDefinition(resolver),
		new SetSourceFilterActionDefinition(resolver),
		new GetInputVolumeActionDefinition(resolver, variables),
		new GetSourceFilterStateActionDefinition(resolver, variables),
		new GetInputMuteActionDefinition(resolver, variables),
		new GetSourceVisibilityActionDefinition(resolver, variables),

		new ObsStateAction("toggle-studio-mode",
			AppStrings.Integrations.Obs.Actions.ToggleStudioMode.Name(),
			AppStrings.Integrations.Obs.Actions.ToggleStudioMode.Description(),
			resolver,
			c => c.SetStudioModeAsync(!c.State.StudioModeActive),
			ActionStates.OnOff,
			state => StateId(ActionStates.OnOff, state.StudioModeActive))
	];

	// Pause is a sub-state of recording rather than a fourth thing, so both recording toggles read the
	// same three-state set: stopped, running, and running-but-paused.
	private static string RecordingStateId(ObsState state) => state switch
	{
		{ IsRecording: true, RecordingPaused: true } => "paused",
		{ IsRecording: true } => "recording",
		_ => "not-recording"
	};

	private static string StateId(IReadOnlyList<ActionStateDefinition> states, bool active)
		=> active ? states[1].Id : states[0].Id;
}
