namespace MacroDeckHost.Integrations.Meld;

internal static class MeldObjects
{
	public const string IntegrationId = "app.macro-deck.meld";

	public const string Object = "meld";

	public const string ShowScene = "showScene";
	public const string SetStagedScene = "setStagedScene";
	public const string ShowStagedScene = "showStagedScene";
	public const string ToggleMute = "toggleMute";
	public const string SetMuted = "setMuted";
	public const string ToggleMonitor = "toggleMonitor";
	public const string ToggleLayer = "toggleLayer";
	public const string ToggleEffect = "toggleEffect";
	public const string RegisterTrackObserver = "registerTrackObserver";
	public const string UnregisterTrackObserver = "unregisterTrackObserver";
	public const string SetGain = "setGain";
	public const string SendCommand = "sendCommand";
	public const string SetProperty = "setProperty";

	public const string GainUpdatedSignal = "gainUpdated";

	public const string VersionProperty = "version";
	public const string SessionProperty = "session";
	public const string IsStreamingProperty = "isStreaming";
	public const string IsRecordingProperty = "isRecording";

	public const string CommandScreenshot = "meld.screenshot";
	public const string CommandScreenshotVertical = "meld.screenshot.vertical";
	public const string CommandStartStreaming = "meld.startStreamingAction";
	public const string CommandStopStreaming = "meld.stopStreamingAction";
	public const string CommandToggleStreaming = "meld.toggleStreamingAction";
	public const string CommandStartRecording = "meld.startRecordingAction";
	public const string CommandStopRecording = "meld.stopRecordingAction";
	public const string CommandToggleRecording = "meld.toggleRecordingAction";

	public const string PropertyMuted = "muted";
	public const string PropertyMonitoring = "monitoring";
}
