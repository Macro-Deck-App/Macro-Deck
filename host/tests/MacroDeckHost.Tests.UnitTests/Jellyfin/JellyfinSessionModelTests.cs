using MacroDeckHost.Integrations.Jellyfin;
using MacroDeckHost.Integrations.Jellyfin.Protocol;
using static MacroDeckHost.Tests.UnitTests.Jellyfin.JellyfinSessions;

namespace MacroDeckHost.Tests.UnitTests.Jellyfin;

[TestFixture]
internal sealed class JellyfinSessionModelTests
{
	[Test]
	public void An_episode_reads_as_series_and_season_with_the_series_poster()
	{
		var item = JellyfinSessionMapper.MapItem(Episode());

		Assert.Multiple(() =>
		{
			Assert.That(item.Name, Is.EqualTo("Pilot"));
			Assert.That(item.DisplayArtists, Is.EqualTo(new[] { "Test Show" }));
			Assert.That(item.DisplayAlbum, Is.EqualTo("Season 1"));
			Assert.That(item.ArtworkItemId, Is.EqualTo("series-1"));
		});
	}

	[Test]
	public void A_track_reads_as_artist_and_album_with_the_album_cover()
	{
		var item = JellyfinSessionMapper.MapItem(Track());

		Assert.Multiple(() =>
		{
			Assert.That(item.DisplayArtists, Is.EqualTo(new[] { "Test Artist" }));
			Assert.That(item.DisplayAlbum, Is.EqualTo("Test Album"));
			Assert.That(item.ArtworkItemId, Is.EqualTo("album-1"));
		});
	}

	[Test]
	public void A_movie_has_no_artist_and_shows_its_year()
	{
		var item = JellyfinSessionMapper.MapItem(Movie());

		Assert.Multiple(() =>
		{
			Assert.That(item.DisplayArtists, Is.Empty);
			Assert.That(item.DisplayAlbum, Is.EqualTo("2008"));
			Assert.That(item.ArtworkItemId, Is.EqualTo("movie-1"));
		});
	}

	[Test]
	public void Macro_Decks_own_session_is_not_listed()
	{
		var sessions = JellyfinSessionMapper.Map([Session("macro-deck-device"), Session("tv")], "macro-deck-device");

		Assert.That(sessions.Select(session => session.DeviceId), Is.EqualTo(new[] { "tv" }));
	}

	[Test]
	public void A_playing_position_moves_on_between_progress_reports()
	{
		var checkIn = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
		var dto = Session("tv", item: Movie(), positionTicks: TimeSpan.FromMinutes(1).Ticks) with
		{
			LastPlaybackCheckIn = checkIn
		};
		var session = JellyfinSessionMapper.Map(dto);

		Assert.That(session.PositionAt(checkIn.AddSeconds(30)), Is.EqualTo(TimeSpan.FromSeconds(90)));
	}

	[Test]
	public void A_server_clock_that_runs_ahead_does_not_move_the_position_backwards_or_freeze_it()
	{
		var received = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
		var dto = Session("tv", item: Movie(), positionTicks: TimeSpan.FromMinutes(1).Ticks) with
		{
			LastPlaybackCheckIn = received.AddMinutes(5)
		};
		var session = JellyfinSessionMapper.Map([dto], "x", received).Single();

		Assert.That(session.PositionAt(received.AddSeconds(10)), Is.EqualTo(TimeSpan.FromSeconds(70)));
	}

	[Test]
	public void A_client_that_reports_progress_rarely_keeps_moving_between_reports()
	{
		var checkIn = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
		var dto = Session("tv", item: Movie(), positionTicks: TimeSpan.FromMinutes(1).Ticks) with
		{
			LastPlaybackCheckIn = checkIn
		};
		var session = JellyfinSessionMapper.Map([dto], "x", checkIn.AddSeconds(25)).Single();

		Assert.That(session.PositionAt(checkIn.AddSeconds(26)), Is.EqualTo(TimeSpan.FromSeconds(86)));
	}
}

[TestFixture]
internal sealed class JellyfinEventDiffTests
{
	private static readonly Guid _entry = Guid.NewGuid();

	[Test]
	public void The_first_successful_read_is_a_baseline_without_session_events()
	{
		var events = Diff(JellyfinServerState.Initial, State(Session("tv", item: Movie())));

		Assert.That(events.Select(e => e.Id), Is.EqualTo(new[] { JellyfinEventIds.Connected }));
	}

	[Test]
	public void Playback_changes_raise_one_event_each_with_their_context()
	{
		var idle = State(Session("tv"));
		var playing = State(Session("tv", item: Movie()));
		var paused = State(Session("tv", item: Movie(), paused: true));
		var resumed = State(Session("tv", item: Movie()));
		var next = State(Session("tv", item: Movie("movie-2", "Sintel")));
		var stopped = State(Session("tv"));

		var ids = new[]
		{
			Diff(idle, playing), Diff(playing, paused), Diff(paused, resumed), Diff(resumed, next), Diff(next, stopped)
		}.Select(events => events.Single().Id);

		Assert.That(ids, Is.EqualTo(new[]
		{
			JellyfinEventIds.PlaybackStarted, JellyfinEventIds.PlaybackPaused, JellyfinEventIds.PlaybackResumed,
			JellyfinEventIds.ItemChanged, JellyfinEventIds.PlaybackStopped
		}));

		var started = Diff(idle, playing).Single().Payload;
		Assert.Multiple(() =>
		{
			Assert.That(started[JellyfinEventPayload.Configuration], Is.EqualTo(_entry.ToString("D")));
			Assert.That(started[JellyfinEventPayload.Device], Is.EqualTo("Living Room TV"));
			Assert.That(started[JellyfinEventPayload.User], Is.EqualTo("alex"));
			Assert.That(started[JellyfinEventPayload.ItemName], Is.EqualTo("Big Buck Bunny"));
			Assert.That(started[JellyfinEventPayload.ItemType], Is.EqualTo("Movie"));
		});
	}

	[Test]
	public void A_client_reconnecting_with_a_new_session_id_is_not_a_disconnect()
	{
		var before = State(Session("tv", sessionId: "old"));
		var after = State(Session("tv", sessionId: "new"));

		Assert.That(Diff(before, after), Is.Empty);
	}

	[Test]
	public void A_client_leaving_while_playing_stops_playback_and_disconnects()
	{
		var before = State(Session("tv", item: Movie()), Session("phone"));
		var after = State(Session("phone"));

		Assert.That(Diff(before, after).Select(e => e.Id),
			Is.EqualTo(new[] { JellyfinEventIds.PlaybackStopped, JellyfinEventIds.SessionDisconnected }));
	}

	[Test]
	public void A_short_outage_raises_no_server_events_but_a_settled_one_does()
	{
		var connected = State(Session("tv"));
		var reconnecting = connected with { Status = JellyfinConnectionStatus.Reconnecting };
		var down = connected with { Status = JellyfinConnectionStatus.Disconnected };

		Assert.Multiple(() =>
		{
			Assert.That(Diff(connected, reconnecting), Is.Empty);
			Assert.That(Diff(reconnecting, connected), Is.Empty);
			Assert.That(Diff(reconnecting, down).Select(e => e.Id), Is.EqualTo(new[] { JellyfinEventIds.Disconnected }));
			Assert.That(Diff(down, connected).Select(e => e.Id), Is.EqualTo(new[] { JellyfinEventIds.Connected }));
		});
	}

	private static IReadOnlyList<JellyfinEvent> Diff(JellyfinServerState previous, JellyfinServerState current)
		=> JellyfinEventDiff.Compute(_entry, "Home", previous, current);

	private static JellyfinServerState State(params JellyfinSessionDto[] sessions)
		=> new(JellyfinConnectionStatus.Connected,
			JellyfinSessionMapper.Map(sessions, "macro-deck-device"),
			DateTimeOffset.UtcNow);
}

[TestFixture]
internal sealed class JellyfinDeviceRegistryTests
{
	private static readonly DateTimeOffset _now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

	[Test]
	public void Only_controllable_devices_are_remembered_and_survive_a_reload()
	{
		var registry = new JellyfinDeviceRegistry();
		registry.Observe(Map(Session("tv", "Living Room TV"), Session("dash", "Dashboard", controllable: false)),
			_now,
			_ => false);

		var reloaded = new JellyfinDeviceRegistry();
		reloaded.Load(registry.Serialize());

		Assert.That(reloaded.Devices.Select(device => (device.DeviceId, device.Key)),
			Is.EqualTo(new[] { ("tv", "living_room_tv") }));
	}

	[Test]
	public void Devices_sharing_a_name_get_distinct_keys_that_do_not_change_later()
	{
		var registry = new JellyfinDeviceRegistry();
		registry.Observe(Map(Session("a", "Chrome")), _now, _ => false);
		registry.Observe(Map(Session("a", "Chrome"), Session("b", "Chrome")), _now, _ => false);
		registry.Observe(Map(Session("b", "Chrome")), _now.AddDays(1), _ => false);

		Assert.Multiple(() =>
		{
			Assert.That(registry.Find("a")!.Key, Is.EqualTo("chrome"));
			Assert.That(registry.Find("b")!.Key, Is.EqualTo("chrome_2"));
		});
	}

	[Test]
	public void A_key_another_server_already_uses_is_skipped()
	{
		var registry = new JellyfinDeviceRegistry();
		registry.Observe(Map(Session("a", "TV")), _now, key => key == "tv");

		Assert.That(registry.Find("a")!.Key, Is.EqualTo("tv_2"));
	}

	[Test]
	public void A_device_unseen_for_more_than_thirty_days_is_forgotten_and_a_returning_one_keeps_its_key()
	{
		var registry = new JellyfinDeviceRegistry();
		registry.Observe(Map(Session("old", "Old TV"), Session("back", "Kitchen")), _now, _ => false);

		registry.Observe([], _now.AddDays(31), _ => false);
		registry.Load(registry.Serialize());

		Assert.That(registry.Devices, Is.Empty);

		var kept = new JellyfinDeviceRegistry();
		kept.Observe(Map(Session("back", "Kitchen")), _now, _ => false);
		kept.Observe([], _now.AddDays(10), _ => false);
		kept.Observe(Map(Session("back", "Kitchen speaker")), _now.AddDays(11), _ => false);

		Assert.That(kept.Find("back")!.Key, Is.EqualTo("kitchen"));
	}

	private static IReadOnlyList<JellyfinSession> Map(params JellyfinSessionDto[] sessions)
		=> JellyfinSessionMapper.Map(sessions, "macro-deck-device");
}
