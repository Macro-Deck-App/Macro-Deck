using System.Text.Json;

namespace MacroDeckHost.Integrations.StreamlabsDesktop.Protocol;

internal enum StreamlabsStreamingState
{
	Offline,
	Starting,
	Live,
	Ending,
	Reconnecting
}

internal enum StreamlabsRecordingState
{
	Offline,
	Starting,
	Recording,
	Stopping
}

internal enum StreamlabsReplayBufferState
{
	Offline,
	Running,
	Stopping,
	Saving
}

internal sealed record StreamlabsScene(string Id, string ResourceId, string Name);

internal sealed record StreamlabsSceneItem(
	string SceneId,
	string SceneItemId,
	string SourceId,
	string ResourceId,
	string Name,
	bool Visible);

internal sealed record StreamlabsSource(string SourceId, string Name);

internal sealed record StreamlabsAudioSource(
	string SourceId,
	string ResourceId,
	string Name,
	bool Muted,
	double Deflection);

internal sealed record StreamlabsStreamingModel(
	StreamlabsStreamingState Streaming,
	DateTimeOffset? StreamingSince,
	StreamlabsRecordingState Recording,
	DateTimeOffset? RecordingSince,
	StreamlabsReplayBufferState ReplayBuffer);

internal static class StreamlabsModelReader
{
	public static StreamlabsStreamingState ReadStreamingState(JsonElement value)
		=> ReadString(value) switch
		{
			"starting" => StreamlabsStreamingState.Starting,
			"live" => StreamlabsStreamingState.Live,
			"ending" => StreamlabsStreamingState.Ending,
			"reconnecting" => StreamlabsStreamingState.Reconnecting,
			_ => StreamlabsStreamingState.Offline
		};

	public static StreamlabsRecordingState ReadRecordingState(JsonElement value)
		=> ReadString(value) switch
		{
			"starting" => StreamlabsRecordingState.Starting,
			"recording" => StreamlabsRecordingState.Recording,
			"stopping" => StreamlabsRecordingState.Stopping,
			_ => StreamlabsRecordingState.Offline
		};

	public static StreamlabsReplayBufferState ReadReplayBufferState(JsonElement value)
		=> ReadString(value) switch
		{
			"running" => StreamlabsReplayBufferState.Running,
			"stopping" => StreamlabsReplayBufferState.Stopping,
			"saving" => StreamlabsReplayBufferState.Saving,
			_ => StreamlabsReplayBufferState.Offline
		};

	public static string ToWireString(StreamlabsStreamingState state) => state switch
	{
		StreamlabsStreamingState.Starting => "starting",
		StreamlabsStreamingState.Live => "live",
		StreamlabsStreamingState.Ending => "ending",
		StreamlabsStreamingState.Reconnecting => "reconnecting",
		_ => "offline"
	};

	public static string ToWireString(StreamlabsRecordingState state) => state switch
	{
		StreamlabsRecordingState.Starting => "starting",
		StreamlabsRecordingState.Recording => "recording",
		StreamlabsRecordingState.Stopping => "stopping",
		_ => "offline"
	};

	public static string ToWireString(StreamlabsReplayBufferState state) => state switch
	{
		StreamlabsReplayBufferState.Running => "running",
		StreamlabsReplayBufferState.Stopping => "stopping",
		StreamlabsReplayBufferState.Saving => "saving",
		_ => "offline"
	};

	public static StreamlabsStreamingModel ReadStreamingModel(JsonElement value) => new(
		ReadStreamingState(Property(value, "streamingStatus")),
		ReadTimestamp(Property(value, "streamingStatusTime")),
		ReadRecordingState(Property(value, "recordingStatus")),
		ReadTimestamp(Property(value, "recordingStatusTime")),
		ReadReplayBufferState(Property(value, "replayBufferStatus")));

	public static bool ReadStudioMode(JsonElement value)
	{
		var studioMode = Property(value, "studioMode");
		return studioMode.ValueKind == JsonValueKind.True;
	}

	public static StreamlabsScene? ReadScene(JsonElement value)
	{
		if (value.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		var id = ReadString(Property(value, "id"));
		var name = ReadString(Property(value, "name"));
		if (id is null || name is null)
		{
			return null;
		}

		var resourceId = ReadString(Property(value, "resourceId")) ?? StreamlabsRpcFrames.SceneResource(id);
		return new StreamlabsScene(id, resourceId, name);
	}

	public static StreamlabsSource? ReadSource(JsonElement value)
	{
		if (value.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		var id = ReadString(Property(value, "sourceId")) ?? ReadString(Property(value, "id"));
		var name = ReadString(Property(value, "name"));
		return id is null || name is null ? null : new StreamlabsSource(id, name);
	}

	public static StreamlabsAudioSource? ReadAudioSource(JsonElement value)
	{
		if (value.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		var id = ReadString(Property(value, "sourceId")) ?? ReadString(Property(value, "id"));
		if (id is null)
		{
			return null;
		}

		var resourceId = ReadString(Property(value, "resourceId")) ?? StreamlabsRpcFrames.AudioSourceResource(id);
		var name = ReadString(Property(value, "name")) ?? id;
		var muted = Property(value, "muted").ValueKind == JsonValueKind.True;
		var deflection = ReadNumber(Property(Property(value, "fader"), "deflection")) ?? 0d;

		return new StreamlabsAudioSource(id, resourceId, name, muted, Math.Clamp(deflection, 0d, 1d));
	}

	public static StreamlabsSceneItem? ReadSceneItem(
		JsonElement value,
		string sceneId,
		Func<string, string?> lookupName)
	{
		if (value.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		var sceneItemId = ReadString(Property(value, "sceneItemId")) ?? ReadString(Property(value, "id"));
		var sourceId = ReadString(Property(value, "sourceId"));
		if (sceneItemId is null || sourceId is null)
		{
			return null;
		}

		var owningSceneId = ReadString(Property(value, "sceneId")) ?? sceneId;
		var resourceId = ReadString(Property(value, "resourceId")) ??
			StreamlabsRpcFrames.SceneItemResource(owningSceneId, sceneItemId, sourceId);

		var name = lookupName(sourceId) ?? ReadString(Property(value, "name")) ?? sourceId;
		var visible = Property(value, "visible").ValueKind != JsonValueKind.False;

		return new StreamlabsSceneItem(owningSceneId, sceneItemId, sourceId, resourceId, name, visible);
	}

	public static bool TryReadMuted(JsonElement value, out bool muted)
	{
		var property = Property(value, "muted");
		muted = property.ValueKind == JsonValueKind.True;
		return property.ValueKind is JsonValueKind.True or JsonValueKind.False;
	}

	public static JsonElement Property(JsonElement value, string name)
		=> value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property)
			? property
			: default;

	public static string? ReadString(JsonElement value)
		=> value.ValueKind == JsonValueKind.String ? value.GetString() : null;

	public static double? ReadNumber(JsonElement value)
		=> value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) ? number : null;

	private static DateTimeOffset? ReadTimestamp(JsonElement value)
		=> value.ValueKind == JsonValueKind.String && value.TryGetDateTimeOffset(out var timestamp)
			? timestamp
			: null;
}
