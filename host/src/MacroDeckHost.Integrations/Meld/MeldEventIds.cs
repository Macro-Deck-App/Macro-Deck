namespace MacroDeckHost.Integrations.Meld;

internal static class MeldEventIds
{
	public const string Connected = "connected";

	public const string Disconnected = "disconnected";

	public const string SceneChanged = "scene-changed";

	public const string StagedSceneChanged = "staged-scene-changed";

	public const string StreamingStarted = "streaming-started";

	public const string StreamingStopped = "streaming-stopped";

	public const string RecordingStarted = "recording-started";

	public const string RecordingStopped = "recording-stopped";

	public const string LayerVisibilityChanged = "layer-visibility-changed";

	public const string EffectStateChanged = "effect-state-changed";

	public const string TrackMuteChanged = "track-mute-changed";

	public const string TrackMonitoringChanged = "track-monitoring-changed";

	public const string SessionChanged = "session-changed";
}
