using MacroDeckHost.Integrations.Obs;
using MacroDeck.Sdk.Events;

namespace MacroDeckHost.Tests.UnitTests.Obs;

[TestFixture]
public class ObsEventEmitterTests
{
	private static readonly string[] _connectedOnly = ["connected"];
	private static readonly string[] _disconnectedOnly = ["disconnected"];

	private static readonly string[] _recordingAndStreaming =
		["recording-started", "streaming-started", "recording-stopped"];

	private static readonly string[] _bufferCamStudio =
		["replay-buffer-started", "virtual-cam-started", "studio-mode-changed"];

	private RecordingPublisher _publisher = null!;
	private ObsEventEmitter _emitter = null!;

	[SetUp]
	public void SetUp()
	{
		_publisher = new RecordingPublisher();
		_emitter = new ObsEventEmitter(_publisher);
	}

	private static ObsState Connected(
		string? currentScene = null,
		string? previewScene = null,
		bool isRecording = false,
		string? recordingTimecode = null,
		bool isStreaming = false,
		bool replayBuffer = false,
		bool virtualCam = false,
		bool studioMode = false) => new()
	{
		IsConnected = true,
		CurrentScene = currentScene,
		PreviewScene = previewScene,
		IsRecording = isRecording,
		RecordingTimecode = recordingTimecode,
		IsStreaming = isStreaming,
		ReplayBufferActive = replayBuffer,
		VirtualCamActive = virtualCam,
		StudioModeActive = studioMode
	};

	private void Seed(ObsState state)
	{
		_emitter.Observe(state);
		_publisher.Published.Clear();
	}

	[Test]
	public void The_first_snapshot_reports_the_connection_but_not_the_state_it_found()
	{
		_emitter.Observe(Connected(currentScene: "Live", isRecording: true));

		Assert.That(_publisher.Published.Select(p => p.EventId), Is.EqualTo(_connectedOnly));
	}

	[Test]
	public void A_scene_switch_reports_both_scenes()
	{
		Seed(Connected(currentScene: "Starting Soon"));

		_emitter.Observe(Connected(currentScene: "Live"));

		Assert.Multiple(() =>
		{
			Assert.That(_publisher.Published, Has.Count.EqualTo(1));
			Assert.That(_publisher.Published[0].EventId, Is.EqualTo("scene-changed"));
			Assert.That(_publisher.Published[0].Parameters?["sceneName"], Is.EqualTo("Live"));
			Assert.That(_publisher.Published[0].Parameters?["previousSceneName"], Is.EqualTo("Starting Soon"));
		});
	}

	[Test]
	public void Recording_and_streaming_transitions_each_report_once()
	{
		Seed(Connected());

		_emitter.Observe(Connected(isRecording: true, isStreaming: true));
		_emitter.Observe(Connected(isStreaming: true));

		Assert.That(_publisher.Published.Select(p => p.EventId), Is.EqualTo(_recordingAndStreaming));
	}

	[Test]
	public void Replay_buffer_virtual_cam_and_studio_mode_transitions_are_reported()
	{
		Seed(Connected());

		_emitter.Observe(Connected(replayBuffer: true, virtualCam: true, studioMode: true));

		Assert.Multiple(() =>
		{
			Assert.That(_publisher.Published.Select(p => p.EventId), Is.EqualTo(_bufferCamStudio));
			Assert.That(_publisher.Published[2].Parameters?["enabled"], Is.EqualTo(true));
		});
	}

	[Test]
	public void A_moving_timecode_emits_nothing()
	{
		Seed(Connected(isRecording: true, recordingTimecode: "00:00:01"));

		_emitter.Observe(Connected(isRecording: true, recordingTimecode: "00:00:02"));

		Assert.That(_publisher.Published, Is.Empty);
	}

	[Test]
	public void An_unchanged_snapshot_emits_nothing()
	{
		Seed(Connected(currentScene: "Live"));

		_emitter.Observe(Connected(currentScene: "Live"));

		Assert.That(_publisher.Published, Is.Empty);
	}

	[Test]
	public void Losing_the_connection_reports_only_the_disconnect()
	{
		Seed(Connected(currentScene: "Live", isRecording: true));

		_emitter.Observe(ObsState.Disconnected);

		Assert.That(_publisher.Published.Select(p => p.EventId), Is.EqualTo(_disconnectedOnly));
	}

	[Test]
	public void A_reconnect_does_not_replay_the_state_it_finds()
	{
		Seed(Connected());
		_emitter.Observe(ObsState.Disconnected);
		_emitter.Reset();
		_publisher.Published.Clear();

		_emitter.Observe(Connected(currentScene: "Live", isRecording: true));

		Assert.That(_publisher.Published.Select(p => p.EventId), Is.EqualTo(_connectedOnly));
	}

	[Test]
	public void Mute_and_replay_saved_carry_what_the_state_snapshot_cannot()
	{
		_emitter.PublishInputMuteChanged("Mic/Aux", true);
		_emitter.PublishReplayBufferSaved("/clips/replay.mkv");

		Assert.Multiple(() =>
		{
			Assert.That(_publisher.Published[0].EventId, Is.EqualTo("input-mute-changed"));
			Assert.That(_publisher.Published[0].Parameters?["inputName"], Is.EqualTo("Mic/Aux"));
			Assert.That(_publisher.Published[0].Parameters?["muted"], Is.EqualTo(true));
			Assert.That(_publisher.Published[1].EventId, Is.EqualTo("replay-buffer-saved"));
			Assert.That(_publisher.Published[1].Parameters?["path"], Is.EqualTo("/clips/replay.mkv"));
		});
	}

	private sealed class RecordingPublisher : IEventPublisher
	{
		public List<(string EventId, IReadOnlyDictionary<string, object?>? Parameters)> Published { get; } = [];

		public void Publish(string eventId, IReadOnlyDictionary<string, object?>? parameters = null)
			=> Published.Add((eventId, parameters));
	}
}
