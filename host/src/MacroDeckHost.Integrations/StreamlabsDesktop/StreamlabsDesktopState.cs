using MacroDeckHost.Integrations.StreamlabsDesktop.Protocol;

namespace MacroDeckHost.Integrations.StreamlabsDesktop;

internal sealed record StreamlabsDesktopState
{
	public static StreamlabsDesktopState Disconnected { get; } = new();

	public bool IsConnected { get; init; }

	public string? CurrentScene { get; init; }

	public string? CurrentSceneId { get; init; }

	public int SceneCount { get; init; }

	public StreamlabsStreamingState Streaming { get; init; }

	public DateTimeOffset? StreamingSince { get; init; }

	public StreamlabsRecordingState Recording { get; init; }

	public DateTimeOffset? RecordingSince { get; init; }

	public StreamlabsReplayBufferState ReplayBuffer { get; init; }

	public bool StudioModeActive { get; init; }

	public bool IsStreaming =>
		Streaming is StreamlabsStreamingState.Live or StreamlabsStreamingState.Reconnecting;

	public bool IsRecording => Recording is StreamlabsRecordingState.Recording;

	public bool ReplayBufferActive =>
		ReplayBuffer is StreamlabsReplayBufferState.Running or StreamlabsReplayBufferState.Saving;
}
