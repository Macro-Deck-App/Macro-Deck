using System.Text.Json;
using MacroDeckHost.Integrations.Meld;
using MacroDeck.Sdk.Events;

namespace MacroDeckHost.Tests.UnitTests.Meld;

[TestFixture]
internal sealed class MeldEventEmitterTests
{
	private const string OneSceneOneTrack =
		"""
		{"items":{
		"scene1":{"current":true,"index":0,"name":"Scene One","staged":false,"type":"scene"},
		"track1":{"name":"Track One","muted":false,"monitoring":false,"type":"track"}
		}}
		""";

	private const string TwoScenesSceneTwoCurrent =
		"""
		{"items":{
		"scene1":{"current":false,"index":0,"name":"Scene One","staged":false,"type":"scene"},
		"scene2":{"current":true,"index":1,"name":"Scene Two","staged":false,"type":"scene"},
		"track1":{"name":"Track One","muted":false,"monitoring":false,"type":"track"}
		}}
		""";

	private RecordingPublisher _publisher = null!;
	private MeldEventEmitter _emitter = null!;

	[SetUp]
	public void SetUp()
	{
		_publisher = new RecordingPublisher();
		_emitter = new MeldEventEmitter(_publisher);
	}

	[Test]
	public void The_first_snapshot_publishes_only_connected()
	{
		_emitter.Observe(Connected(OneSceneOneTrack));

		Assert.That(_publisher.Published.Select(p => p.EventId), Is.EqualTo(new[] { MeldEventIds.Connected }));
	}

	[Test]
	public void The_first_snapshot_publishes_nothing_when_it_is_already_disconnected()
	{
		_emitter.Observe(MeldState.Disconnected);

		Assert.That(_publisher.Published, Is.Empty);
	}

	[Test]
	public void A_disconnect_publishes_only_disconnected_even_when_the_scene_also_differs()
	{
		_emitter.Observe(Connected(OneSceneOneTrack));
		_publisher.Published.Clear();

		_emitter.Observe(MeldState.Disconnected);

		Assert.That(_publisher.Published.Select(p => p.EventId), Is.EqualTo(new[] { MeldEventIds.Disconnected }));
	}

	[Test]
	public void Reset_makes_the_next_snapshot_a_first_snapshot_again()
	{
		_emitter.Observe(Connected(OneSceneOneTrack));
		_emitter.Reset();
		_publisher.Published.Clear();

		_emitter.Observe(Connected(OneSceneOneTrack));

		Assert.That(_publisher.Published.Select(p => p.EventId), Is.EqualTo(new[] { MeldEventIds.Connected }));
	}

	[Test]
	public void A_scene_change_publishes_scene_changed_with_the_previous_scene()
	{
		_emitter.Observe(Connected(OneSceneOneTrack));
		_publisher.Published.Clear();

		_emitter.Observe(Connected(TwoScenesSceneTwoCurrent));

		Assert.That(_publisher.Published.Select(p => p.EventId), Does.Contain(MeldEventIds.SceneChanged));
		var (eventId, parameters) = _publisher.Published.Single(p => p.EventId == MeldEventIds.SceneChanged);
		Assert.Multiple(() =>
		{
			Assert.That(eventId, Is.EqualTo(MeldEventIds.SceneChanged));
			Assert.That(parameters!["sceneId"], Is.EqualTo("scene2"));
			Assert.That(parameters["sceneName"], Is.EqualTo("Scene Two"));
			Assert.That(parameters["previousSceneId"], Is.EqualTo("scene1"));
			Assert.That(parameters["previousSceneName"], Is.EqualTo("Scene One"));
		});
	}

	[Test]
	public void A_staged_scene_change_publishes_staged_scene_changed()
	{
		const string staged =
			"""
			{"items":{
			"scene1":{"current":true,"index":0,"name":"Scene One","staged":false,"type":"scene"},
			"scene2":{"current":false,"index":1,"name":"Scene Two","staged":true,"type":"scene"}
			}}
			""";

		_emitter.Observe(Connected(OneSceneOneTrack));
		_publisher.Published.Clear();

		_emitter.Observe(Connected(staged));

		Assert.That(_publisher.Published.Select(p => p.EventId), Does.Contain(MeldEventIds.StagedSceneChanged));
		var (_, parameters) = _publisher.Published.Single(p => p.EventId == MeldEventIds.StagedSceneChanged);
		Assert.Multiple(() =>
		{
			Assert.That(parameters!["sceneId"], Is.EqualTo("scene2"));
			Assert.That(parameters["sceneName"], Is.EqualTo("Scene Two"));
		});
	}

	[Test]
	public void Streaming_and_recording_transitions_publish_started_and_stopped()
	{
		_emitter.Observe(Connected(OneSceneOneTrack));
		_publisher.Published.Clear();

		_emitter.Observe(Connected(OneSceneOneTrack, isStreaming: true, isRecording: true));

		Assert.That(_publisher.Published.Select(p => p.EventId),
			Is.EquivalentTo(new[] { MeldEventIds.StreamingStarted, MeldEventIds.RecordingStarted }));

		_publisher.Published.Clear();
		_emitter.Observe(Connected(OneSceneOneTrack));

		Assert.That(_publisher.Published.Select(p => p.EventId),
			Is.EquivalentTo(new[] { MeldEventIds.StreamingStopped, MeldEventIds.RecordingStopped }));
	}

	[Test]
	public void A_layer_visibility_flip_publishes_once_with_layer_scene_and_visible()
	{
		const string layerHidden =
			"""
			{"items":{
			"scene1":{"current":true,"index":0,"name":"Scene","staged":false,"type":"scene"},
			"layer1":{"parent":"scene1","index":0,"name":"Layer","visible":true,"type":"layer"}
			}}
			""";
		const string layerShown =
			"""
			{"items":{
			"scene1":{"current":true,"index":0,"name":"Scene","staged":false,"type":"scene"},
			"layer1":{"parent":"scene1","index":0,"name":"Layer","visible":false,"type":"layer"}
			}}
			""";

		_emitter.Observe(Connected(layerHidden));
		_publisher.Published.Clear();

		_emitter.Observe(Connected(layerShown));

		Assert.That(_publisher.Published, Has.Count.EqualTo(1));
		var (eventId, parameters) = _publisher.Published[0];
		Assert.Multiple(() =>
		{
			Assert.That(eventId, Is.EqualTo(MeldEventIds.LayerVisibilityChanged));
			Assert.That(parameters!["layerId"], Is.EqualTo("layer1"));
			Assert.That(parameters["layerName"], Is.EqualTo("Layer"));
			Assert.That(parameters["sceneId"], Is.EqualTo("scene1"));
			Assert.That(parameters["sceneName"], Is.EqualTo("Scene"));
			Assert.That(parameters["visible"], Is.EqualTo(false));
		});
	}

	[Test]
	public void An_effect_state_flip_publishes_once_with_effect_layer_and_scene()
	{
		const string disabled =
			"""
			{"items":{
			"scene1":{"current":true,"index":0,"name":"Scene","staged":false,"type":"scene"},
			"layer1":{"parent":"scene1","index":0,"name":"Layer","visible":true,"type":"layer"},
			"effect1":{"parent":"layer1","name":"Effect","enabled":false,"type":"effect"}
			}}
			""";
		const string enabled =
			"""
			{"items":{
			"scene1":{"current":true,"index":0,"name":"Scene","staged":false,"type":"scene"},
			"layer1":{"parent":"scene1","index":0,"name":"Layer","visible":true,"type":"layer"},
			"effect1":{"parent":"layer1","name":"Effect","enabled":true,"type":"effect"}
			}}
			""";

		_emitter.Observe(Connected(disabled));
		_publisher.Published.Clear();

		_emitter.Observe(Connected(enabled));

		Assert.That(_publisher.Published, Has.Count.EqualTo(1));
		var (eventId, parameters) = _publisher.Published[0];
		Assert.Multiple(() =>
		{
			Assert.That(eventId, Is.EqualTo(MeldEventIds.EffectStateChanged));
			Assert.That(parameters!["effectId"], Is.EqualTo("effect1"));
			Assert.That(parameters["layerId"], Is.EqualTo("layer1"));
			Assert.That(parameters["sceneId"], Is.EqualTo("scene1"));
			Assert.That(parameters["enabled"], Is.EqualTo(true));
		});
	}

	[Test]
	public void A_gain_update_with_unchanged_mute_publishes_nothing()
	{
		_emitter.Observe(Connected(OneSceneOneTrack));
		_publisher.Published.Clear();

		_emitter.ObserveTrackMute("track1", "Track One", false);

		Assert.That(_publisher.Published, Is.Empty);
	}

	[Test]
	public void A_mute_change_from_the_session_diff_then_ObserveTrackMute_publishes_exactly_once()
	{
		const string muted =
			"""
			{"items":{
			"scene1":{"current":true,"index":0,"name":"Scene","staged":false,"type":"scene"},
			"track1":{"name":"Track One","muted":true,"monitoring":false,"type":"track"}
			}}
			""";

		_emitter.Observe(Connected(OneSceneOneTrack));
		_publisher.Published.Clear();

		_emitter.Observe(Connected(muted));
		_emitter.ObserveTrackMute("track1", "Track One", true);

		Assert.That(_publisher.Published.Count(p => p.EventId == MeldEventIds.TrackMuteChanged), Is.EqualTo(1));
	}

	[Test]
	public void A_mute_change_from_ObserveTrackMute_then_the_session_diff_publishes_exactly_once()
	{
		const string muted =
			"""
			{"items":{
			"scene1":{"current":true,"index":0,"name":"Scene","staged":false,"type":"scene"},
			"track1":{"name":"Track One","muted":true,"monitoring":false,"type":"track"}
			}}
			""";

		_emitter.Observe(Connected(OneSceneOneTrack));
		_publisher.Published.Clear();

		_emitter.ObserveTrackMute("track1", "Track One", true);
		_emitter.Observe(Connected(muted));

		Assert.That(_publisher.Published.Count(p => p.EventId == MeldEventIds.TrackMuteChanged), Is.EqualTo(1));
	}

	[Test]
	public void A_track_monitoring_flip_publishes_track_monitoring_changed()
	{
		const string monitoring =
			"""
			{"items":{
			"scene1":{"current":true,"index":0,"name":"Scene","staged":false,"type":"scene"},
			"track1":{"name":"Track One","muted":false,"monitoring":true,"type":"track"}
			}}
			""";

		_emitter.Observe(Connected(OneSceneOneTrack));
		_publisher.Published.Clear();

		_emitter.Observe(Connected(monitoring));

		Assert.That(_publisher.Published.Select(p => p.EventId), Does.Contain(MeldEventIds.TrackMonitoringChanged));
	}

	[Test]
	public void Session_changed_fires_on_rename_and_not_on_a_visibility_flip()
	{
		const string renamed =
			"""
			{"items":{
			"scene1":{"current":true,"index":0,"name":"Renamed Scene","staged":false,"type":"scene"},
			"track1":{"name":"Track One","muted":false,"monitoring":false,"type":"track"}
			}}
			""";
		const string layerFlip =
			"""
			{"items":{
			"scene1":{"current":true,"index":0,"name":"Scene","staged":false,"type":"scene"},
			"layer1":{"parent":"scene1","index":0,"name":"Layer","visible":true,"type":"layer"}
			}}
			""";
		const string layerFlipped =
			"""
			{"items":{
			"scene1":{"current":true,"index":0,"name":"Scene","staged":false,"type":"scene"},
			"layer1":{"parent":"scene1","index":0,"name":"Layer","visible":false,"type":"layer"}
			}}
			""";

		_emitter.Observe(Connected(OneSceneOneTrack));
		_publisher.Published.Clear();
		_emitter.Observe(Connected(renamed));
		Assert.That(_publisher.Published.Select(p => p.EventId), Does.Contain(MeldEventIds.SessionChanged));

		_emitter.Reset();
		_emitter.Observe(Connected(layerFlip));
		_publisher.Published.Clear();
		_emitter.Observe(Connected(layerFlipped));
		Assert.That(_publisher.Published.Select(p => p.EventId), Does.Not.Contain(MeldEventIds.SessionChanged));
	}

	[Test]
	public void A_publisher_that_throws_does_not_propagate()
	{
		_publisher.ThrowOnPublish = new InvalidOperationException("boom");

		Assert.DoesNotThrow(() => _emitter.Observe(Connected(OneSceneOneTrack)));
	}

	private static MeldState Connected(string sessionJson, bool isStreaming = false, bool isRecording = false)
	{
		using var document = JsonDocument.Parse(sessionJson);
		return new MeldState
		{
			IsConnected = true,
			ApiVersion = 1,
			SupportsSetMuted = true,
			SupportsSetProperty = false,
			IsStreaming = isStreaming,
			IsRecording = isRecording,
			Session = MeldSessionParser.Parse(document.RootElement.Clone())
		};
	}

	private sealed class RecordingPublisher : IEventPublisher
	{
		public List<(string EventId, IReadOnlyDictionary<string, object?>? Parameters)> Published { get; } = [];

		public Exception? ThrowOnPublish { get; set; }

		public void Publish(string eventId, IReadOnlyDictionary<string, object?>? parameters = null)
		{
			Published.Add((eventId, parameters));
			if (ThrowOnPublish is { } exception)
			{
				throw exception;
			}
		}
	}
}
