using MacroDeckHost.Application.Triggers.Providers;
using MacroDeckHost.Application.Ui.Transport.Messages.MusicPlayer;

namespace MacroDeckHost.Tests.UnitTests.Triggers;

[TestFixture]
public class MusicPlayerEventProviderTests
{
	private static readonly string[] _playbackTransitions =
	[
		"music-player::playback-started",
		"music-player::playback-paused",
		"music-player::playback-stopped"
	];

	private static readonly string[] _disconnectThenConnect =
		["music-player::player-disconnected", "music-player::player-connected"];

	private RecordingEventBus _bus = null!;
	private MusicPlayerEventProvider _provider = null!;

	[SetUp]
	public void SetUp()
	{
		_bus = new RecordingEventBus();
		_provider = new MusicPlayerEventProvider(_bus);
	}

	private static MusicPlayerStatePayload State(
		string instanceId = "spotify::acc",
		bool connected = true,
		string playbackState = "playing",
		string? track = "Track A",
		string? artist = "Artist A",
		string? device = "Desktop",
		int? volume = 50) => new()
	{
		InstanceId = instanceId,
		IsConnected = connected,
		PlaybackState = playbackState,
		TrackName = track,
		ArtistName = artist,
		DeviceName = device,
		Volume = volume
	};

	private void Seed(MusicPlayerStatePayload state)
	{
		_provider.Observe(state);
		_bus.Published.Clear();
	}

	private IEnumerable<string> PublishedIds => _bus.Published.Select(o => o.EventId);

	[Test]
	public void The_first_observation_of_an_instance_publishes_nothing()
	{
		_provider.Observe(State());

		Assert.That(_bus.Published, Is.Empty);
	}

	[Test]
	public void A_new_track_reports_both_tracks_and_the_instance()
	{
		Seed(State(track: "Track A"));

		_provider.Observe(State(track: "Track B", artist: "Artist B"));

		var published = _bus.Published.Single();

		Assert.Multiple(() =>
		{
			Assert.That(published.EventId, Is.EqualTo("music-player::track-changed"));
			Assert.That(published.Parameters["trackName"], Is.EqualTo("Track B"));
			Assert.That(published.Parameters["previousTrackName"], Is.EqualTo("Track A"));
			Assert.That(published.Parameters["artistName"], Is.EqualTo("Artist B"));
			Assert.That(published.Parameters["instance"], Is.EqualTo("spotify::acc"));
		});
	}

	[Test]
	public void Playback_transitions_map_to_started_paused_and_stopped()
	{
		Seed(State(playbackState: "paused"));

		_provider.Observe(State(playbackState: "playing"));
		_provider.Observe(State(playbackState: "paused"));
		_provider.Observe(State(playbackState: "stopped"));

		Assert.That(PublishedIds, Is.EqualTo(_playbackTransitions));
	}

	[Test]
	public void Device_and_volume_changes_are_reported_with_their_previous_values()
	{
		Seed(State(device: "Desktop", volume: 50));

		_provider.Observe(State(device: "Phone", volume: 80));

		Assert.Multiple(() =>
		{
			var device = _bus.Published.First(o => o.EventId.EndsWith("device-changed", StringComparison.Ordinal));
			Assert.That(device.Parameters["deviceName"], Is.EqualTo("Phone"));
			Assert.That(device.Parameters["previousDeviceName"], Is.EqualTo("Desktop"));

			var volume = _bus.Published.First(o => o.EventId.EndsWith("volume-changed", StringComparison.Ordinal));
			Assert.That(volume.Parameters["volume"], Is.EqualTo(80d));
			Assert.That(volume.Parameters["previousVolume"], Is.EqualTo(50d));
		});
	}

	[Test]
	public void An_unchanged_state_publishes_nothing()
	{
		Seed(State());

		_provider.Observe(State());

		Assert.That(_bus.Published, Is.Empty);
	}

	[Test]
	public void Losing_and_regaining_the_connection_reports_only_the_connection()
	{
		Seed(State());

		_provider.Observe(State(connected: false, track: null, device: null, volume: null));
		_provider.Observe(State());

		Assert.That(PublishedIds, Is.EqualTo(_disconnectThenConnect));
	}

	[Test]
	public void A_payload_without_an_instance_id_is_ignored()
	{
		_provider.Observe(State(instanceId: ""));
		_provider.Observe(State(instanceId: "", track: "Track B"));

		Assert.That(_bus.Published, Is.Empty);
	}

	[Test]
	public void Two_instances_are_diffed_independently()
	{
		Seed(State("spotify::acc", track: "Track A"));
		Seed(State("sinusbot::bot", track: "Track Z"));

		_provider.Observe(State("spotify::acc", track: "Track B"));

		Assert.Multiple(() =>
		{
			Assert.That(_bus.Published, Has.Count.EqualTo(1));
			Assert.That(_bus.Published[0].Parameters["instance"], Is.EqualTo("spotify::acc"));
		});
	}

	[Test]
	public void A_forgotten_instance_starts_from_scratch()
	{
		Seed(State(track: "Track A"));

		_provider.Forget([]);
		_provider.Observe(State(track: "Track B"));

		Assert.That(_bus.Published, Is.Empty);
	}
}
