using MacroDeckHost.Integrations.Jellyfin.Connections;
using MacroDeckHost.Integrations.Jellyfin.Events;
using MacroDeckHost.Integrations.Jellyfin.Protocol;
using MacroDeckHost.Integrations.Jellyfin.State;
using static MacroDeckHost.Tests.UnitTests.Jellyfin.JellyfinSessions;

namespace MacroDeckHost.Tests.UnitTests.Jellyfin;

[TestFixture]
internal sealed class JellyfinSessionModelTests
{
	[Test]
	public void A_session_keeps_the_address_the_server_reports_and_a_blank_one_is_unknown()
	{
		var sessions = JellyfinSessionMapper.Map([
				Session("a") with { RemoteEndPoint = " 192.168.1.20 " },
				Session("b") with { RemoteEndPoint = "  " }
			],
			"macro-deck-device");

		Assert.That(sessions.Select(session => session.Address), Is.EqualTo(new[] { "192.168.1.20", null }));
	}

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

		Assert.That(ids,
			Is.EqualTo(new[]
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
			Assert.That(Diff(reconnecting, down).Select(e => e.Id),
				Is.EqualTo(new[] { JellyfinEventIds.Disconnected }));
			Assert.That(Diff(down, connected).Select(e => e.Id), Is.EqualTo(new[] { JellyfinEventIds.Connected }));
		});
	}

	private static IReadOnlyList<JellyfinEvent> Diff(JellyfinServerState previous, JellyfinServerState current)
	{
		return JellyfinEventDiff.Compute(_entry, "Home", previous, current);
	}

	private static JellyfinServerState State(params JellyfinSessionDto[] sessions)
	{
		return new JellyfinServerState(JellyfinConnectionStatus.Connected,
			JellyfinSessionMapper.Map(sessions, "macro-deck-device"),
			DateTimeOffset.UtcNow);
	}
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
	public void Same_named_clients_playing_at_once_get_distinct_keys_that_do_not_change_later()
	{
		var registry = new JellyfinDeviceRegistry();
		registry.Observe(Map(Session("a", "Chrome", Movie(), lastActivity: _now)), _now, _ => false);
		registry.Observe(Map(Session("a", "Chrome", Movie(), lastActivity: _now.AddMinutes(1)),
				Session("b", "Chrome", Movie(), lastActivity: _now.AddMinutes(2))),
			_now,
			_ => false);
		registry.Observe(Map(Session("a", "Chrome", lastActivity: _now.AddMinutes(3)),
				Session("b", "Chrome", lastActivity: _now.AddMinutes(4))),
			_now.AddDays(1),
			_ => false);

		Assert.Multiple(() =>
		{
			Assert.That(registry.Find("a")!.Key, Is.EqualTo("chrome"));
			Assert.That(registry.Find("b")!.Key, Is.EqualTo("chrome_2"));
		});
	}

	[Test]
	public void A_cast_that_returns_under_a_new_id_keeps_its_device_while_the_old_session_is_still_listed()
	{
		var registry = new JellyfinDeviceRegistry();
		registry.Observe(Map(Cast("first", _now)), _now, _ => false);
		var original = registry.Find("first")!;

		var bothListed = Map(Cast("first", _now), Cast("second", _now.AddMinutes(5)));
		registry.Observe(bothListed, _now, _ => false);
		registry.Observe(bothListed, _now, _ => false);
		registry.Load(registry.Serialize());

		Assert.Multiple(() =>
		{
			Assert.That(registry.Devices, Has.Count.EqualTo(1));
			Assert.That(registry.Find("first"), Is.Null);
			Assert.That(registry.Find("second")!.Key, Is.EqualTo(original.Key));
			Assert.That(registry.Find("second")!.LocalId, Is.EqualTo(original.LocalId));
		});
	}

	[Test]
	public void Same_named_idle_clients_from_one_address_share_one_device_that_follows_the_latest_activity()
	{
		var registry = new JellyfinDeviceRegistry();
		registry.Observe(Map(At(Session("a", "Chrome", lastActivity: _now), "10.0.0.5")), _now, _ => false);

		registry.Observe(Map(At(Session("a", "Chrome", lastActivity: _now), "10.0.0.5"),
				At(Session("b", "Chrome", lastActivity: _now.AddMinutes(1)), "10.0.0.5")),
			_now,
			_ => false);
		var followsB = registry.Find("b");

		registry.Observe(Map(At(Session("a", "Chrome", lastActivity: _now.AddMinutes(2)), "10.0.0.5"),
				At(Session("b", "Chrome", lastActivity: _now.AddMinutes(1)), "10.0.0.5")),
			_now,
			_ => false);

		Assert.Multiple(() =>
		{
			Assert.That(followsB?.Key, Is.EqualTo("chrome"));
			Assert.That(registry.Devices.Select(device => (device.DeviceId, device.Key)),
				Is.EqualTo(new[] { ("a", "chrome") }));
		});
	}

	[Test]
	public void Same_named_casts_at_different_addresses_listed_together_stay_separate_devices()
	{
		var registry = new JellyfinDeviceRegistry();
		registry.Observe(Map(At(Cast("kitchen", _now), "10.0.0.11")), _now, _ => false);
		var kitchen = registry.Find("kitchen")!;

		registry.Observe(Map(At(Cast("kitchen", _now), "10.0.0.11"),
				At(Cast("bedroom", _now.AddMinutes(1)), "10.0.0.12")),
			_now,
			_ => false);
		registry.Observe(Map(At(Cast("kitchen", _now.AddMinutes(2)), "10.0.0.11"),
				At(Cast("bedroom", _now.AddMinutes(1)), "10.0.0.12")),
			_now,
			_ => false);

		Assert.Multiple(() =>
		{
			Assert.That(registry.Devices, Has.Count.EqualTo(2));
			Assert.That(registry.Find("kitchen"), Is.EqualTo(kitchen));
			Assert.That(registry.Find("bedroom")!.Key, Is.EqualTo("google_cast_2"));
			Assert.That(registry.Find("bedroom")!.LocalId, Is.Not.EqualTo(kitchen.LocalId));
		});
	}

	[Test]
	public void A_cast_returning_under_a_new_id_from_its_address_keeps_its_device_while_the_old_session_is_listed()
	{
		var registry = new JellyfinDeviceRegistry();
		registry.Observe(Map(At(Cast("first", _now), "10.0.0.11")), _now, _ => false);
		var original = registry.Find("first")!;

		registry.Observe(Map(At(Cast("first", _now), "10.0.0.11"),
				At(Cast("second", _now.AddMinutes(5)), "10.0.0.11")),
			_now,
			_ => false);

		Assert.Multiple(() =>
		{
			Assert.That(registry.Devices, Has.Count.EqualTo(1));
			Assert.That(registry.Find("second")!.LocalId, Is.EqualTo(original.LocalId));
		});
	}

	[Test]
	public void A_namesake_from_another_address_does_not_take_over_a_stored_device_that_is_not_listed()
	{
		var registry = new JellyfinDeviceRegistry();
		registry.Observe(Map(At(Cast("kitchen", _now), "10.0.0.11")), _now, _ => false);
		registry.Load(registry.Serialize());

		registry.Observe(Map(At(Cast("bedroom", _now.AddMinutes(1)), "10.0.0.12")), _now, _ => false);

		Assert.Multiple(() =>
		{
			Assert.That(registry.Devices.Select(device => device.DeviceId),
				Is.EquivalentTo(new[] { "kitchen", "bedroom" }));
			Assert.That(registry.Find("kitchen")!.Address, Is.EqualTo("10.0.0.11"));
		});
	}

	[Test]
	public void A_namesake_from_the_same_address_takes_over_a_stored_device_that_is_not_listed()
	{
		var registry = new JellyfinDeviceRegistry();
		registry.Observe(Map(At(Cast("first", _now), "10.0.0.11")), _now, _ => false);
		var original = registry.Find("first")!;
		registry.Load(registry.Serialize());

		registry.Observe(Map(At(Cast("second", _now.AddMinutes(1)), "10.0.0.11")), _now, _ => false);

		Assert.Multiple(() =>
		{
			Assert.That(registry.Devices.Select(device => device.DeviceId), Is.EqualTo(new[] { "second" }));
			Assert.That(registry.Find("second")!.Key, Is.EqualTo(original.Key));
		});
	}

	[Test]
	public void A_stored_device_without_an_address_still_matches_its_namesake_and_learns_the_address()
	{
		var registry = new JellyfinDeviceRegistry();
		registry.Load("""
					  [
					  	{"DeviceId":"x1","Name":"Google Cast","Client":"Chromecast","Key":"google_cast","LastSeen":"2026-10-07T00:00:00+00:00","IsControllable":true}
					  ]
					  """);

		registry.Observe(Map(At(Cast("new", _now), "10.0.0.11")), _now, _ => false);
		registry.Load(registry.Serialize());

		Assert.Multiple(() =>
		{
			Assert.That(registry.Devices.Select(device => device.DeviceId), Is.EqualTo(new[] { "new" }));
			Assert.That(registry.Find("new")!.Address, Is.EqualTo("10.0.0.11"));
		});
	}

	[Test]
	public void A_new_id_takes_over_the_idle_namesake_and_leaves_the_playing_one_alone()
	{
		var registry = new JellyfinDeviceRegistry();
		registry.Observe(Map(Cast("p", _now, Movie()), Cast("q", _now, Movie())), _now, _ => false);
		var playingKey = registry.Find("p")!.Key;
		var idleKey = registry.Find("q")!.Key;

		registry.Observe(Map(Cast("p", _now.AddMinutes(1), Movie()),
				Cast("q", _now.AddMinutes(1)),
				Cast("r", _now.AddMinutes(2))),
			_now,
			_ => false);

		Assert.Multiple(() =>
		{
			Assert.That(registry.Devices, Has.Count.EqualTo(2));
			Assert.That(registry.Find("p")!.Key, Is.EqualTo(playingKey));
			Assert.That(registry.Find("r")!.Key, Is.EqualTo(idleKey));
		});
	}

	[Test]
	public void Among_equally_recent_stored_duplicates_the_one_with_the_base_key_is_taken_over()
	{
		var registry = new JellyfinDeviceRegistry();
		registry.Load("""
					  [
					  	{"DeviceId":"x3","Name":"Google Cast","Client":"Chromecast","Key":"google_cast_3","LastSeen":"2026-10-07T00:00:00+00:00","IsControllable":true},
					  	{"DeviceId":"x1","Name":"Google Cast","Client":"Chromecast","Key":"google_cast","LastSeen":"2026-10-07T00:00:00+00:00","IsControllable":true},
					  	{"DeviceId":"x2","Name":"Google Cast","Client":"Chromecast","Key":"google_cast_2","LastSeen":"2026-10-07T00:00:00+00:00","IsControllable":true}
					  ]
					  """);

		registry.Observe(Map(Cast("new", _now)), _now, _ => false);

		Assert.Multiple(() =>
		{
			Assert.That(registry.Devices, Has.Count.EqualTo(3));
			Assert.That(registry.Find("new")!.Key, Is.EqualTo("google_cast"));
			Assert.That(registry.Find("new")!.LocalId, Is.EqualTo(JellyfinDeviceRegistry.LocalIdFor("x1")));
			Assert.That(registry.Find("x2")!.LocalId, Is.EqualTo(JellyfinDeviceRegistry.LocalIdFor("x2")));
		});
	}

	[Test]
	public void A_new_cast_follows_its_listed_predecessor_and_leftovers_leave_stored_duplicates_alone()
	{
		var registry = new JellyfinDeviceRegistry();
		registry.Load("""
					  [
					  	{"DeviceId":"x1","Name":"Google Cast","Client":"Chromecast","Key":"google_cast","LastSeen":"2026-10-07T00:00:00+00:00","IsControllable":true},
					  	{"DeviceId":"x2","Name":"Google Cast","Client":"Chromecast","Key":"google_cast_2","LastSeen":"2026-10-07T00:00:00+00:00","IsControllable":true}
					  ]
					  """);

		registry.Observe(Map(Cast("c1", _now)), _now, _ => false);
		registry.Observe(Map(Cast("c1", _now), Cast("c2", _now.AddMinutes(1))), _now, _ => false);
		registry.Observe(Map(Cast("c1", _now), Cast("c2", _now.AddMinutes(1)), Cast("c3", _now.AddMinutes(2), Movie())),
			_now,
			_ => false);
		registry.Observe(Map(Cast("c1", _now), Cast("c2", _now.AddMinutes(1)), Cast("c3", _now.AddMinutes(2), Movie())),
			_now,
			_ => false);

		Assert.That(registry.Devices.Select(device => (device.DeviceId, device.Key)),
			Is.EquivalentTo(new[] { ("c3", "google_cast"), ("x2", "google_cast_2") }));
	}

	[Test]
	public void A_same_named_client_of_another_app_is_a_device_of_its_own()
	{
		var registry = new JellyfinDeviceRegistry();
		registry.Observe(Map(Cast("cast", _now)), _now, _ => false);
		registry.Observe(Map(Session("web", "Google Cast", lastActivity: _now.AddMinutes(1))), _now, _ => false);

		Assert.That(registry.Devices.Select(device => device.DeviceId), Is.EquivalentTo(new[] { "cast", "web" }));
	}

	[Test]
	public void A_device_reusing_an_id_that_was_taken_over_gets_its_own_local_id()
	{
		var registry = new JellyfinDeviceRegistry();
		registry.Observe(Map(Cast("x", _now)), _now, _ => false);
		registry.Observe(Map(Cast("x", _now), Cast("y", _now.AddMinutes(1))), _now, _ => false);

		registry.Observe(Map(Cast("y", _now.AddMinutes(2), Movie()),
				Session("x", "Kitchen", lastActivity: _now.AddMinutes(3))),
			_now,
			_ => false);
		registry.Load(registry.Serialize());

		Assert.Multiple(() =>
		{
			Assert.That(registry.FindByLocalId(JellyfinDeviceRegistry.LocalIdFor("x"))!.DeviceId, Is.EqualTo("y"));
			Assert.That(registry.Find("x")!.LocalId, Is.Not.EqualTo(registry.Find("y")!.LocalId));
			Assert.That(registry.FindByLocalId(registry.Find("x")!.LocalId)!.DeviceId, Is.EqualTo("x"));
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
	{
		return JellyfinSessionMapper.Map(sessions, "macro-deck-device");
	}

	private static JellyfinSessionDto At(JellyfinSessionDto session, string address)
	{
		return session with { RemoteEndPoint = address };
	}

	private static JellyfinSessionDto Cast(string deviceId, DateTimeOffset lastActivity, JellyfinItemDto? item = null)
	{
		return Session(deviceId, "Google Cast", item, lastActivity: lastActivity) with { Client = "Chromecast" };
	}
}
