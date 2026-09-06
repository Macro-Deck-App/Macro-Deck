using MacroDeck.Localization;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.StreamlabsDesktop;

internal static class StreamlabsDesktopVariables
{
	public const string Prefix = "streamlabs_";

	public const string IsConnected = Prefix + "is_connected";

	public const string CurrentScene = Prefix + "current_scene";

	public const string SceneCount = Prefix + "scene_count";

	public const string IsStreaming = Prefix + "is_streaming";

	public const string StreamingStatus = Prefix + "streaming_status";

	public const string StreamingSeconds = Prefix + "streaming_seconds";

	public const string IsRecording = Prefix + "is_recording";

	public const string RecordingStatus = Prefix + "recording_status";

	public const string RecordingSeconds = Prefix + "recording_seconds";

	public const string ReplayBufferActive = Prefix + "replay_buffer_active";

	public const string ReplayBufferStatus = Prefix + "replay_buffer_status";

	public const string StudioModeActive = Prefix + "studio_mode_active";

	private static readonly TimeSpan _fast = TimeSpan.FromSeconds(1);
	private static readonly TimeSpan _normal = TimeSpan.FromSeconds(2);
	private static readonly TimeSpan _slow = TimeSpan.FromSeconds(5);

	public static IReadOnlyList<VariableDefinition> All { get; } =
	[
		VariableDefinition.Eager(IsConnected, VariableType.Boolean, refreshInterval: _slow)
			with
			{
				DisplayName = MacroDeckStrings.Connection.Connected()
			},
		VariableDefinition.Eager(CurrentScene, VariableType.Text, refreshInterval: _normal)
			with
			{
				DisplayName = AppStrings.Integrations.StreamlabsDesktop.Variables.CurrentScene()
			},
		VariableDefinition.Eager(SceneCount, VariableType.Numeric, 0, TimeSpan.FromSeconds(10))
			with
			{
				DisplayName = AppStrings.Integrations.StreamlabsDesktop.Variables.SceneCount()
			},
		VariableDefinition.Eager(IsStreaming, VariableType.Boolean, refreshInterval: _normal)
			with
			{
				DisplayName = AppStrings.Integrations.StreamlabsDesktop.Variables.IsStreaming()
			},
		VariableDefinition.Eager(StreamingStatus, VariableType.Text, refreshInterval: _normal)
			with
			{
				DisplayName = AppStrings.Integrations.StreamlabsDesktop.Variables.StreamingStatus()
			},
		VariableDefinition.Eager(StreamingSeconds, VariableType.Numeric, 0, _fast)
			with
			{
				DisplayName = AppStrings.Integrations.StreamlabsDesktop.Variables.StreamingSeconds()
			},
		VariableDefinition.Eager(IsRecording, VariableType.Boolean, refreshInterval: _normal)
			with
			{
				DisplayName = AppStrings.Integrations.StreamlabsDesktop.Variables.IsRecording()
			},
		VariableDefinition.Eager(RecordingStatus, VariableType.Text, refreshInterval: _normal)
			with
			{
				DisplayName = AppStrings.Integrations.StreamlabsDesktop.Variables.RecordingStatus()
			},
		VariableDefinition.Eager(RecordingSeconds, VariableType.Numeric, 0, _fast)
			with
			{
				DisplayName = AppStrings.Integrations.StreamlabsDesktop.Variables.RecordingSeconds()
			},
		VariableDefinition.Eager(ReplayBufferActive, VariableType.Boolean, refreshInterval: _slow)
			with
			{
				DisplayName = AppStrings.Integrations.StreamlabsDesktop.Variables.ReplayBufferActive()
			},
		VariableDefinition.Eager(ReplayBufferStatus, VariableType.Text, refreshInterval: _slow)
			with
			{
				DisplayName = AppStrings.Integrations.StreamlabsDesktop.Variables.ReplayBufferStatus()
			},
		VariableDefinition.Eager(StudioModeActive, VariableType.Boolean, refreshInterval: _slow)
			with
			{
				DisplayName = AppStrings.Integrations.StreamlabsDesktop.Variables.StudioModeActive()
			}
	];
}
