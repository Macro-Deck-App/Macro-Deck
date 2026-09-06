using MacroDeck.Sdk.Events;

namespace MacroDeckHost.Integrations.Meld;

internal sealed class MeldEventEmitter
{
	private readonly IEventPublisher _publisher;

	private readonly Lock _lock = new();

	private readonly Dictionary<string, bool> _lastMuted = new(StringComparer.Ordinal);
	private readonly Dictionary<string, bool> _lastMonitoring = new(StringComparer.Ordinal);

	private MeldState? _previous;

	public MeldEventEmitter(IEventPublisher publisher)
	{
		_publisher = publisher;
	}

	public void Observe(MeldState current)
	{
		MeldState? previous;
		lock (_lock)
		{
			previous = _previous;
			_previous = current;

			if (previous is null)
			{
				if (current.IsConnected)
				{
					SeedTrackBaselines(current.Session);
				}
			}
		}

		if (previous is null)
		{
			if (current.IsConnected)
			{
				Publish(MeldEventIds.Connected);
			}

			return;
		}

		if (previous.IsConnected != current.IsConnected)
		{
			Publish(current.IsConnected ? MeldEventIds.Connected : MeldEventIds.Disconnected);

			return;
		}

		if (!current.IsConnected)
		{
			return;
		}

		var previousSession = previous.Session;
		var currentSession = current.Session;

		if (previousSession.CurrentSceneId != currentSession.CurrentSceneId)
		{
			PublishSceneEvent(MeldEventIds.SceneChanged,
				previousSession,
				currentSession,
				currentSession.CurrentSceneId);
		}

		if (previousSession.StagedSceneId != currentSession.StagedSceneId)
		{
			PublishSceneEvent(MeldEventIds.StagedSceneChanged,
				previousSession,
				currentSession,
				currentSession.StagedSceneId);
		}

		PublishToggle(previous.IsStreaming,
			current.IsStreaming,
			MeldEventIds.StreamingStarted,
			MeldEventIds.StreamingStopped);
		PublishToggle(previous.IsRecording,
			current.IsRecording,
			MeldEventIds.RecordingStarted,
			MeldEventIds.RecordingStopped);

		foreach (var (id, currentLayer) in currentSession.LayersById)
		{
			if (previousSession.LayersById.TryGetValue(id, out var previousLayer) &&
				previousLayer.Visible != currentLayer.Visible)
			{
				Publish(MeldEventIds.LayerVisibilityChanged,
					new Dictionary<string, object?>(StringComparer.Ordinal)
					{
						["layerId"] = currentLayer.Id,
						["layerName"] = currentLayer.Name,
						["sceneId"] = currentLayer.SceneId,
						["sceneName"] = currentLayer.SceneName,
						["visible"] = currentLayer.Visible
					});
			}
		}

		foreach (var (id, currentEffect) in currentSession.EffectsById)
		{
			if (previousSession.EffectsById.TryGetValue(id, out var previousEffect) &&
				previousEffect.Enabled != currentEffect.Enabled)
			{
				Publish(MeldEventIds.EffectStateChanged,
					new Dictionary<string, object?>(StringComparer.Ordinal)
					{
						["effectId"] = currentEffect.Id,
						["effectName"] = currentEffect.Name,
						["layerId"] = currentEffect.LayerId,
						["layerName"] = currentEffect.LayerName,
						["sceneId"] = currentEffect.SceneId,
						["enabled"] = currentEffect.Enabled
					});
			}
		}

		foreach (var track in currentSession.Tracks)
		{
			ObserveTrackMute(track.Id, track.Name, track.Muted);
			ObserveTrackMonitoring(track.Id, track.Name, track.Monitoring);
		}

		if (previousSession.StructuralHash != currentSession.StructuralHash)
		{
			Publish(MeldEventIds.SessionChanged);
		}
	}

	public void ObserveTrackMute(string trackId, string trackName, bool muted)
	{
		bool changed;
		lock (_lock)
		{
			if (_lastMuted.TryGetValue(trackId, out var previous))
			{
				changed = previous != muted;
			}
			else
			{
				changed = false;
			}

			_lastMuted[trackId] = muted;
		}

		if (changed)
		{
			Publish(MeldEventIds.TrackMuteChanged,
				new Dictionary<string, object?>(StringComparer.Ordinal)
				{
					["trackId"] = trackId,
					["trackName"] = trackName,
					["muted"] = muted
				});
		}
	}

	public void Reset()
	{
		lock (_lock)
		{
			_previous = null;
			_lastMuted.Clear();
			_lastMonitoring.Clear();
		}
	}

	private void SeedTrackBaselines(MeldSession session)
	{
		foreach (var track in session.Tracks)
		{
			_lastMuted[track.Id] = track.Muted;
			_lastMonitoring[track.Id] = track.Monitoring;
		}
	}

	private void ObserveTrackMonitoring(string trackId, string trackName, bool monitoring)
	{
		bool changed;
		lock (_lock)
		{
			changed = _lastMonitoring.TryGetValue(trackId, out var previous) && previous != monitoring;
			_lastMonitoring[trackId] = monitoring;
		}

		if (changed)
		{
			Publish(MeldEventIds.TrackMonitoringChanged,
				new Dictionary<string, object?>(StringComparer.Ordinal)
				{
					["trackId"] = trackId,
					["trackName"] = trackName,
					["monitoring"] = monitoring
				});
		}
	}

	private void PublishSceneEvent(string eventId,
		MeldSession previousSession,
		MeldSession currentSession,
		string? sceneId)
	{
		if (sceneId is null)
		{
			return;
		}

		currentSession.ScenesById.TryGetValue(sceneId, out var scene);
		var previousSceneId = eventId == MeldEventIds.SceneChanged
			? previousSession.CurrentSceneId
			: previousSession.StagedSceneId;
		var previousSceneName = previousSceneId is not null &&
			previousSession.ScenesById.TryGetValue(previousSceneId, out var previousScene)
				? previousScene.Name
				: string.Empty;

		Publish(eventId,
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				["sceneId"] = sceneId,
				["sceneName"] = scene?.Name ?? string.Empty,
				["previousSceneId"] = previousSceneId ?? string.Empty,
				["previousSceneName"] = previousSceneName
			});
	}

	private void PublishToggle(bool previous, bool current, string onEventId, string offEventId)
	{
		if (previous != current)
		{
			Publish(current ? onEventId : offEventId);
		}
	}

	private void Publish(string eventId, IReadOnlyDictionary<string, object?>? parameters = null)
	{
		try
		{
			_publisher.Publish(eventId, parameters);
		}
		catch (Exception)
		{
			// Deliberately swallowed - see the summary above.
		}
	}
}
