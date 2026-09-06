using MacroDeck.Localization;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Obs;

internal static class ObsVariables
{
	private static readonly Slot[] _slots =
	[
		new("is_connected", VariableType.Boolean, null, TimeSpan.FromSeconds(2)),
		new("current_scene", VariableType.Text, null, TimeSpan.FromSeconds(1)),
		new("preview_scene", VariableType.Text, null, TimeSpan.FromSeconds(1)),
		new("is_recording", VariableType.Boolean, null, TimeSpan.FromSeconds(1)),
		new("recording_paused", VariableType.Boolean, null, TimeSpan.FromSeconds(1)),
		new("recording_timecode", VariableType.Text, null, TimeSpan.FromSeconds(1)),
		new("is_streaming", VariableType.Boolean, null, TimeSpan.FromSeconds(1)),
		new("streaming_timecode", VariableType.Text, null, TimeSpan.FromSeconds(1)),
		new("virtual_camera_active", VariableType.Boolean, null, TimeSpan.FromSeconds(2)),
		new("replay_buffer_active", VariableType.Boolean, null, TimeSpan.FromSeconds(2)),
		new("studio_mode_active", VariableType.Boolean, null, TimeSpan.FromSeconds(2)),
		new("fps", VariableType.Numeric, 0, TimeSpan.FromSeconds(2)),
		new("max_fps", VariableType.Numeric, 2, TimeSpan.FromSeconds(2)),
		new("cpu_usage", VariableType.Numeric, 1, TimeSpan.FromSeconds(2)),
		new("streaming_bitrate", VariableType.Numeric, 0, TimeSpan.FromSeconds(1)),
		new("recording_bitrate", VariableType.Numeric, 0, TimeSpan.FromSeconds(1)),
		new("dropped_frames", VariableType.Numeric, 0, TimeSpan.FromSeconds(1)),
		new("dropped_frames_percent", VariableType.Numeric, 1, TimeSpan.FromSeconds(1)),
		new("skipped_frames", VariableType.Numeric, 0, TimeSpan.FromSeconds(1)),
		new("skipped_frames_percent", VariableType.Numeric, 1, TimeSpan.FromSeconds(1))
	];

	public static IReadOnlyList<VariableDefinition> Templates { get; } =
		Declare(VariableNameTemplate.Placeholder("configuration"), null);

	// The definition id embeds the entry guid ("N" format) as the re-registration identity for upgrades;
	// the VariableConfiguration key below reuses the same "N" format so it lines up with that guid.
	public static IReadOnlyList<VariableDefinition> Declare(string key,
		Guid? entryId,
		LocalizedText configurationName = default)
		=> _slots.Select(slot => VariableDefinition.Eager($"obs_{key}_{slot.Name}",
				slot.Type,
				slot.DecimalPlaces,
				slot.RefreshInterval) with
			{
				DisplayName = DisplayNameFor(slot.Name),
				Id = entryId is { } id
					? $"entry-{id:N}-{slot.Name.Replace('_', '-')}"
					: null,
				Configuration = entryId is { } configId
					? new VariableConfiguration(configId.ToString("N"), configurationName)
					: null
			}).ToList();

	private static LocalizedText DisplayNameFor(string slot) => slot switch
	{
		"is_connected" => MacroDeckStrings.Connection.Connected(),
		"current_scene" => AppStrings.Integrations.Obs.Variables.CurrentScene(),
		"preview_scene" => AppStrings.Integrations.Obs.Variables.PreviewScene(),
		"is_recording" => AppStrings.Integrations.Obs.Variables.IsRecording(),
		"recording_paused" => AppStrings.Integrations.Obs.Variables.RecordingPaused(),
		"recording_timecode" => AppStrings.Integrations.Obs.Variables.RecordingTimecode(),
		"is_streaming" => AppStrings.Integrations.Obs.Variables.IsStreaming(),
		"streaming_timecode" => AppStrings.Integrations.Obs.Variables.StreamingTimecode(),
		"virtual_camera_active" => AppStrings.Integrations.Obs.Variables.VirtualCameraActive(),
		"replay_buffer_active" => AppStrings.Integrations.Obs.Variables.ReplayBufferActive(),
		"studio_mode_active" => AppStrings.Integrations.Obs.Variables.StudioModeActive(),
		"fps" => AppStrings.Integrations.Obs.Variables.Fps(),
		"max_fps" => AppStrings.Integrations.Obs.Variables.MaxFps(),
		"cpu_usage" => AppStrings.Integrations.Obs.Variables.CpuUsage(),
		"streaming_bitrate" => AppStrings.Integrations.Obs.Variables.StreamingBitrate(),
		"recording_bitrate" => AppStrings.Integrations.Obs.Variables.RecordingBitrate(),
		"dropped_frames" => AppStrings.Integrations.Obs.Variables.DroppedFrames(),
		"dropped_frames_percent" => AppStrings.Integrations.Obs.Variables.DroppedFramesPercent(),
		"skipped_frames" => AppStrings.Integrations.Obs.Variables.SkippedFrames(),
		"skipped_frames_percent" => AppStrings.Integrations.Obs.Variables.SkippedFramesPercent(),
		_ => default
	};

	public static bool TryGetConfigurationEntryId(string? definitionId, out Guid entryId)
		=> TryParseDefinitionId(definitionId, out entryId, out _);

	public static bool TrySplit(string definitionId,
		IReadOnlyList<ObsRuntime> runtimes,
		out ObsRuntime runtime,
		out string slot)
	{
		runtime = null!;
		if (!TryParseDefinitionId(definitionId, out var entryId, out slot))
		{
			return false;
		}

		foreach (var candidate in runtimes)
		{
			if (candidate.Id == entryId)
			{
				runtime = candidate;
				return true;
			}
		}

		slot = string.Empty;
		return false;
	}

	private static bool TryParseDefinitionId(string? definitionId, out Guid entryId, out string slot)
	{
		entryId = Guid.Empty;
		slot = string.Empty;
		if (definitionId is null ||
			!definitionId.StartsWith("entry-", StringComparison.Ordinal) ||
			definitionId.Length <= 39 ||
			definitionId[38] != '-' ||
			!Guid.TryParseExact(definitionId.AsSpan(6, 32), "N", out entryId))
		{
			return false;
		}

		var candidate = definitionId[39..].Replace('-', '_');
		if (!_slots.Any(s => string.Equals(s.Name, candidate, StringComparison.Ordinal)))
		{
			return false;
		}

		slot = candidate;
		return true;
	}

	public static object? Read(ObsState state, string slot) => slot switch
	{
		"is_connected" => state.IsConnected,
		"current_scene" => state.CurrentScene,
		"preview_scene" => state.PreviewScene,
		"is_recording" => state.IsRecording,
		"recording_paused" => state.RecordingPaused,
		"recording_timecode" => state.RecordingTimecode,
		"is_streaming" => state.IsStreaming,
		"streaming_timecode" => state.StreamingTimecode,
		"virtual_camera_active" => state.VirtualCamActive,
		"replay_buffer_active" => state.ReplayBufferActive,
		"studio_mode_active" => state.StudioModeActive,
		"fps" => state.IsConnected ? (int)Math.Round(state.Fps) : null,
		"max_fps" => state.IsConnected ? Math.Round(state.MaxFps, 2) : null,
		"cpu_usage" => state.IsConnected ? Math.Round(state.CpuUsage, 1) : null,
		"streaming_bitrate" => state.IsConnected ? (int)Math.Round(state.StreamingBitrateKbps) : null,
		"recording_bitrate" => state.IsConnected ? (int)Math.Round(state.RecordingBitrateKbps) : null,
		"dropped_frames" => state.IsConnected ? state.DroppedFrames : null,
		"dropped_frames_percent" => state.IsConnected ? Math.Round(state.DroppedFramesPercent, 1) : null,
		"skipped_frames" => state.IsConnected ? state.EncoderSkippedFrames : null,
		"skipped_frames_percent" => state.IsConnected
			? Math.Round(state.EncoderSkippedFramesPercent, 1)
			: null,
		_ => null
	};

	private sealed record Slot(string Name, VariableType Type, int? DecimalPlaces, TimeSpan RefreshInterval);
}
