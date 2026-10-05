namespace MacroDeckHost.Integrations.Obs;

internal static class ObsEventIds
{
	public const string Connected = "connected";

	public const string Disconnected = "disconnected";

	public const string SceneChanged = "scene-changed";

	public const string PreviewSceneChanged = "preview-scene-changed";

	public const string RecordingStarted = "recording-started";

	public const string RecordingStopped = "recording-stopped";

	public const string RecordingPaused = "recording-paused";

	public const string RecordingResumed = "recording-resumed";

	public const string StreamingStarted = "streaming-started";

	public const string StreamingStopped = "streaming-stopped";

	public const string ReplayBufferStarted = "replay-buffer-started";

	public const string ReplayBufferStopped = "replay-buffer-stopped";

	public const string ReplayBufferSaved = "replay-buffer-saved";

	public const string VirtualCamStarted = "virtual-cam-started";

	public const string VirtualCamStopped = "virtual-cam-stopped";

	public const string StudioModeChanged = "studio-mode-changed";

	public const string InputMuteChanged = "input-mute-changed";

	public const string InputBecameActive = "input-became-active";

	public const string InputBecameInactive = "input-became-inactive";

	public const string InputStartedShowing = "input-started-showing";

	public const string InputStoppedShowing = "input-stopped-showing";

	public const string CustomEvent = "custom-event";
}
