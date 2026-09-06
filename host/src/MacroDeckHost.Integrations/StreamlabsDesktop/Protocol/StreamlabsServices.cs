namespace MacroDeckHost.Integrations.StreamlabsDesktop.Protocol;

internal static class StreamlabsServices
{
	public const string Scenes = "ScenesService";

	public const string Sources = "SourcesService";

	public const string Audio = "AudioService";

	public const string Streaming = "StreamingService";

	public const string Transitions = "TransitionsService";

	public const string GetScenes = "getScenes";

	public const string ActiveScene = "activeScene";

	public const string MakeSceneActive = "makeSceneActive";

	public const string GetItems = "getItems";

	public const string GetSources = "getSources";

	public const string GetModel = "getModel";

	public const string SetVisibility = "setVisibility";

	public const string SetMuted = "setMuted";

	public const string SetDeflection = "setDeflection";

	public const string ToggleStreaming = "toggleStreaming";

	public const string ToggleRecording = "toggleRecording";

	public const string StartReplayBuffer = "startReplayBuffer";

	public const string StopReplayBuffer = "stopReplayBuffer";

	public const string SaveReplay = "saveReplay";

	public const string EnableStudioMode = "enableStudioMode";

	public const string DisableStudioMode = "disableStudioMode";

	public const string ExecuteStudioModeTransition = "executeStudioModeTransition";

	public const string SceneSwitched = "sceneSwitched";

	public const string SceneAdded = "sceneAdded";

	public const string SceneRemoved = "sceneRemoved";

	public const string ItemAdded = "itemAdded";

	public const string ItemRemoved = "itemRemoved";

	public const string ItemUpdated = "itemUpdated";

	public const string SourceAdded = "sourceAdded";

	public const string SourceUpdated = "sourceUpdated";

	public const string SourceRemoved = "sourceRemoved";

	public const string StreamingStatusChange = "streamingStatusChange";

	public const string RecordingStatusChange = "recordingStatusChange";

	public const string ReplayBufferStatusChange = "replayBufferStatusChange";

	public const string StudioModeChanged = "studioModeChanged";

	public static IReadOnlyList<(string Service, string Observable)> Subscriptions { get; } =
	[
		(Scenes, SceneSwitched),
		(Scenes, SceneAdded),
		(Scenes, SceneRemoved),
		(Scenes, ItemAdded),
		(Scenes, ItemRemoved),
		(Scenes, ItemUpdated),
		(Sources, SourceAdded),
		(Sources, SourceUpdated),
		(Sources, SourceRemoved),
		(Streaming, StreamingStatusChange),
		(Streaming, RecordingStatusChange),
		(Streaming, ReplayBufferStatusChange),
		(Transitions, StudioModeChanged)
	];

	public static string SubscriptionId(string service, string observable) => $"{service}.{observable}";
}
