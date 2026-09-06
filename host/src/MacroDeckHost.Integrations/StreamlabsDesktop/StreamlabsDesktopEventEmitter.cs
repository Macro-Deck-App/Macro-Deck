using MacroDeckHost.Integrations.StreamlabsDesktop.Protocol;
using MacroDeck.Sdk.Events;

namespace MacroDeckHost.Integrations.StreamlabsDesktop;

internal sealed class StreamlabsDesktopEventEmitter
{
	private readonly IEventPublisher _publisher;
	private readonly object _gate = new();
	private readonly Dictionary<(string SceneId, string SceneItemId), bool> _visibility = [];
	private readonly Dictionary<string, bool> _muted = new(StringComparer.Ordinal);

	private bool _ready;

	public StreamlabsDesktopEventEmitter(IEventPublisher publisher)
	{
		_publisher = publisher;
	}

	public void MarkReady()
	{
		lock (_gate)
		{
			_ready = true;
		}
	}

	public void Reset()
	{
		lock (_gate)
		{
			_ready = false;
			_visibility.Clear();
			_muted.Clear();
		}
	}

	public void PublishConnected() => _publisher.Publish(StreamlabsDesktopEventIds.Connected);

	public void PublishDisconnected() => _publisher.Publish(StreamlabsDesktopEventIds.Disconnected);

	public void PublishSceneChanged(string sceneName, string? previousSceneName)
	{
		if (!IsReady())
		{
			return;
		}

		_publisher.Publish(StreamlabsDesktopEventIds.SceneChanged,
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				["sceneName"] = sceneName,
				["previousSceneName"] = previousSceneName ?? string.Empty
			});
	}

	public void PublishStreamingStatus(StreamlabsStreamingState previous, StreamlabsStreamingState current)
	{
		if (previous == current || !IsReady())
		{
			return;
		}

		PublishStatusChanged(StreamlabsDesktopEventIds.StreamingStatusChanged,
			StreamlabsModelReader.ToWireString(current),
			StreamlabsModelReader.ToWireString(previous));

		var wasLive = previous is StreamlabsStreamingState.Live or StreamlabsStreamingState.Reconnecting;
		var isLive = current is StreamlabsStreamingState.Live or StreamlabsStreamingState.Reconnecting;
		if (wasLive != isLive)
		{
			_publisher.Publish(isLive
				? StreamlabsDesktopEventIds.StreamingStarted
				: StreamlabsDesktopEventIds.StreamingStopped);
		}
	}

	public void PublishRecordingStatus(StreamlabsRecordingState previous, StreamlabsRecordingState current)
	{
		if (previous == current || !IsReady())
		{
			return;
		}

		PublishStatusChanged(StreamlabsDesktopEventIds.RecordingStatusChanged,
			StreamlabsModelReader.ToWireString(current),
			StreamlabsModelReader.ToWireString(previous));

		var wasRecording = previous is StreamlabsRecordingState.Recording;
		var isRecording = current is StreamlabsRecordingState.Recording;
		if (wasRecording != isRecording)
		{
			_publisher.Publish(isRecording
				? StreamlabsDesktopEventIds.RecordingStarted
				: StreamlabsDesktopEventIds.RecordingStopped);
		}
	}

	public void PublishReplayBufferStatus(
		StreamlabsReplayBufferState previous,
		StreamlabsReplayBufferState current)
	{
		if (previous == current || !IsReady())
		{
			return;
		}

		var wasActive = previous is StreamlabsReplayBufferState.Running or StreamlabsReplayBufferState.Saving;
		var isActive = current is StreamlabsReplayBufferState.Running or StreamlabsReplayBufferState.Saving;
		if (wasActive != isActive)
		{
			_publisher.Publish(isActive
				? StreamlabsDesktopEventIds.ReplayBufferStarted
				: StreamlabsDesktopEventIds.ReplayBufferStopped);
		}
	}

	public void PublishStudioModeChanged(bool enabled)
	{
		if (!IsReady())
		{
			return;
		}

		_publisher.Publish(StreamlabsDesktopEventIds.StudioModeChanged,
			new Dictionary<string, object?>(StringComparer.Ordinal) { ["enabled"] = enabled });
	}

	public void PublishSourceMuteChanged(string sourceId, string sourceName, bool muted)
	{
		if (!ShouldPublish(_muted, sourceId, muted))
		{
			return;
		}

		_publisher.Publish(StreamlabsDesktopEventIds.SourceMuteChanged,
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				["sourceName"] = sourceName,
				["muted"] = muted
			});
	}

	public void PublishSourceVisibilityChanged(
		string sceneId,
		string sceneItemId,
		string sceneName,
		string sourceName,
		bool visible)
	{
		if (!ShouldPublish(_visibility, (sceneId, sceneItemId), visible))
		{
			return;
		}

		_publisher.Publish(StreamlabsDesktopEventIds.SourceVisibilityChanged,
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				["sceneName"] = sceneName,
				["sourceName"] = sourceName,
				["visible"] = visible
			});
	}

	private void PublishStatusChanged(string eventId, string status, string previousStatus)
		=> _publisher.Publish(eventId,
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				["status"] = status,
				["previousStatus"] = previousStatus
			});

	private bool IsReady()
	{
		lock (_gate)
		{
			return _ready;
		}
	}

	private bool ShouldPublish<TKey>(Dictionary<TKey, bool> seen, TKey key, bool value)
		where TKey : notnull
	{
		lock (_gate)
		{
			var changed = !seen.TryGetValue(key, out var previous) || previous != value;
			seen[key] = value;
			return changed && _ready;
		}
	}
}
