using MacroDeck.Sdk.Events;

namespace MacroDeckHost.Integrations.Obs;

internal sealed class ObsEventEmitter
{
	private readonly IEventPublisher _publisher;
	private readonly string _configuration;
	private ObsState? _previous;

	public ObsEventEmitter(IEventPublisher publisher)
		: this(publisher, Guid.Empty)
	{
	}

	public ObsEventEmitter(IEventPublisher publisher, Guid configurationId)
	{
		_publisher = publisher;
		_configuration = configurationId.ToString("D");
	}

	public void Observe(ObsState current)
	{
		var previous = _previous;
		_previous = current;

		if (previous is null)
		{
			if (current.IsConnected)
			{
				Publish(ObsEventIds.Connected);
			}

			return;
		}

		if (previous.IsConnected != current.IsConnected)
		{
			Publish(current.IsConnected ? ObsEventIds.Connected : ObsEventIds.Disconnected);

			return;
		}

		if (!current.IsConnected)
		{
			return;
		}

		if (previous.CurrentScene != current.CurrentScene && current.CurrentScene is not null)
		{
			Publish(ObsEventIds.SceneChanged,
				new Dictionary<string, object?>(StringComparer.Ordinal)
				{
					["sceneName"] = current.CurrentScene,
					["previousSceneName"] = previous.CurrentScene ?? string.Empty
				});
		}

		if (previous.PreviewScene != current.PreviewScene && current.PreviewScene is not null)
		{
			Publish(ObsEventIds.PreviewSceneChanged,
				new Dictionary<string, object?>(StringComparer.Ordinal)
				{
					["sceneName"] = current.PreviewScene,
					["previousSceneName"] = previous.PreviewScene ?? string.Empty
				});
		}

		PublishToggle(previous.IsRecording,
			current.IsRecording,
			ObsEventIds.RecordingStarted,
			ObsEventIds.RecordingStopped);
		PublishToggle(previous.RecordingPaused,
			current.RecordingPaused,
			ObsEventIds.RecordingPaused,
			ObsEventIds.RecordingResumed);
		PublishToggle(previous.IsStreaming,
			current.IsStreaming,
			ObsEventIds.StreamingStarted,
			ObsEventIds.StreamingStopped);
		PublishToggle(previous.ReplayBufferActive,
			current.ReplayBufferActive,
			ObsEventIds.ReplayBufferStarted,
			ObsEventIds.ReplayBufferStopped);
		PublishToggle(previous.VirtualCamActive,
			current.VirtualCamActive,
			ObsEventIds.VirtualCamStarted,
			ObsEventIds.VirtualCamStopped);

		if (previous.StudioModeActive != current.StudioModeActive)
		{
			Publish(ObsEventIds.StudioModeChanged,
				new Dictionary<string, object?>(StringComparer.Ordinal) { ["enabled"] = current.StudioModeActive });
		}
	}

	public void Reset() => _previous = null;

	public void PublishInputMuteChanged(string inputName, bool muted)
		=> Publish(ObsEventIds.InputMuteChanged,
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				["inputName"] = inputName,
				["muted"] = muted
			});

	public void PublishReplayBufferSaved(string path)
		=> Publish(ObsEventIds.ReplayBufferSaved,
			new Dictionary<string, object?>(StringComparer.Ordinal) { ["path"] = path });

	private void PublishToggle(bool previous, bool current, string onEventId, string offEventId)
	{
		if (previous != current)
		{
			Publish(current ? onEventId : offEventId);
		}
	}

	private void Publish(string eventId, IReadOnlyDictionary<string, object?>? parameters = null)
	{
		var payload = parameters is null
			? new Dictionary<string, object?>(StringComparer.Ordinal)
			: new Dictionary<string, object?>(parameters, StringComparer.Ordinal);
		payload["configuration"] = _configuration;
		_publisher.Publish(eventId, payload);
	}
}
