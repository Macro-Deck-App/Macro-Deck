using MacroDeckHost.Integrations.YtmDesktop;
using MacroDeckHost.Integrations.YtmDesktop.Protocol;
using MacroDeck.Sdk.MusicPlayer;

namespace MacroDeckHost.Tests.UnitTests.YtmDesktop;

[TestFixture]
internal sealed class YtmDesktopConnectionTests
{
	private static readonly TimeSpan _reconnect = TimeSpan.FromMilliseconds(30);
	private static readonly TimeSpan _refresh = TimeSpan.FromMilliseconds(50);

	private const string PlayingState = """
										{
											"player": { "trackState": 1, "videoProgress": 12, "volume": 60, "muted": false, "adPlaying": false,
												"queue": { "repeatMode": 0, "selectedItemIndex": 0, "items": [], "automixItems": [] } },
											"video": { "id": "abc", "title": "Song", "author": "Artist", "album": "Album",
												"likeStatus": 1, "thumbnails": [], "durationSeconds": 200, "isLive": false,
												"videoType": 0, "metadataFilled": true },
											"playlistId": null
										}
										""";

	private const string LikedState = """
									  {
									  	"player": { "trackState": 1, "videoProgress": 12, "volume": 60, "muted": false, "adPlaying": false,
									  		"queue": null },
									  	"video": { "id": "abc", "title": "Song", "author": "Artist", "album": "Album",
									  		"likeStatus": 2, "thumbnails": [], "durationSeconds": 200 },
									  	"playlistId": null
									  }
									  """;

	private const string OtherTrackLikedState = """
												{
													"player": { "trackState": 1, "videoProgress": 0, "volume": 60, "muted": false, "adPlaying": false,
														"queue": null },
													"video": { "id": "xyz", "title": "Other", "author": "Artist", "album": null,
														"likeStatus": 2, "thumbnails": [], "durationSeconds": 100 },
													"playlistId": null
												}
												""";

	private static readonly string[] _afterDelete = ["p2"];

	private readonly List<YtmDesktopConnection> _connections = [];

	private FakeYtmDesktopApiClient _api = null!;
	private FakeYtmDesktopRealtimeClient _realtime = null!;

	[SetUp]
	public void SetUp()
	{
		_api = new FakeYtmDesktopApiClient();
		_realtime = new FakeYtmDesktopRealtimeClient();
	}

	[TearDown]
	public async Task TearDown()
	{
		foreach (var connection in _connections)
		{
			await connection.StopAsync();
		}

		_connections.Clear();
		_api.Dispose();
		_realtime.Dispose();
	}

	[Test]
	public async Task The_socket_handshake_happens_before_the_state_is_seeded()
	{
		_realtime.ConnectException = new IOException("no server");
		var connection = Create();

		connection.Start();
		await WaitForAsync(() => _realtime.ConnectCalls > 0);

		// A failed handshake must not spend one of the five seconds the state endpoint allows a read.
		Assert.That(_api.StateCalls, Is.Zero);
	}

	[Test]
	public async Task A_session_reports_the_seeded_state()
	{
		_api.StateJson = PlayingState;
		var connection = Create();

		connection.Start();
		await WaitForAsync(() => connection.Snapshot.Player.IsConnected);

		Assert.Multiple(() =>
		{
			Assert.That(connection.Snapshot.Player.TrackName, Is.EqualTo("Song"));
			Assert.That(connection.Snapshot.Player.PlaybackState, Is.EqualTo(PlaybackState.Playing));
			Assert.That(_api.UsedToken, Is.EqualTo("token"));
		});
	}

	[Test]
	public async Task A_rate_limited_seed_keeps_reporting_disconnected_rather_than_an_empty_track()
	{
		_api.StateException = FakeYtmDesktopApiClient.RateLimited();
		var connection = Create();

		connection.Start();
		await WaitForAsync(() => connection.IsSessionLive);

		Assert.Multiple(() =>
		{
			Assert.That(connection.Snapshot.Player, Is.EqualTo(MusicPlayerState.Disconnected));
			Assert.That(connection.Snapshot.Player.IsConnected, Is.False);
			Assert.That(connection.IsSessionLive, Is.True);
		});
	}

	[Test]
	public async Task A_failed_seed_is_retried_until_it_lands()
	{
		_api.StateException = FakeYtmDesktopApiClient.RateLimited();
		_api.StateJson = PlayingState;
		var connection = Create();

		connection.Start();
		await WaitForAsync(() => connection.IsSessionLive);

		_api.StateException = null;
		await Task.Delay(_refresh);

		await WaitForAsync(() =>
		{
			connection.TouchState();
			return connection.Snapshot.Player.IsConnected;
		});

		Assert.That(connection.Snapshot.Player.TrackName, Is.EqualTo("Song"));
	}

	[Test]
	public async Task A_pushed_update_replaces_the_snapshot_without_a_single_rest_call()
	{
		var connection = Create();
		connection.Start();
		await WaitForSeededSessionAsync(connection);

		var callsAfterSeed = _api.StateCalls;
		_realtime.RaiseState(PlayingState);

		await Task.Delay(20);

		Assert.Multiple(() =>
		{
			Assert.That(connection.Snapshot.Player.TrackName, Is.EqualTo("Song"));
			Assert.That(_api.StateCalls, Is.EqualTo(callsAfterSeed));
		});
	}

	[Test]
	public async Task A_fresh_snapshot_stops_the_background_refresh()
	{
		var connection = Create();
		connection.Start();
		await WaitForSeededSessionAsync(connection);

		_realtime.RaiseState(PlayingState);
		var callsAfterPush = _api.StateCalls;

		for (var i = 0; i < 20; i++)
		{
			connection.TouchState();
		}

		await Task.Delay(20);

		Assert.That(_api.StateCalls, Is.EqualTo(callsAfterPush));
	}

	[Test]
	public async Task A_stale_snapshot_is_refreshed_at_most_once_per_interval()
	{
		var connection = Create();
		connection.Start();
		await WaitForSeededSessionAsync(connection);

		await Task.Delay(_refresh * 2);
		var before = _api.StateCalls;

		for (var i = 0; i < 20; i++)
		{
			connection.TouchState();
		}

		await Task.Delay(20);

		Assert.That(_api.StateCalls - before, Is.EqualTo(1));
	}

	[Test]
	public async Task The_playlists_are_read_once_and_then_maintained_from_the_events()
	{
		_api.Playlists = [new YtmPlaylist("p1", "Favourites")];
		var connection = Create();

		connection.Start();
		await WaitForAsync(() => connection.Playlists is not null);

		_realtime.RaisePlaylistCreated(new YtmPlaylist("p2", "Workout"));
		_realtime.RaisePlaylistDeleted("p1");

		Assert.Multiple(() =>
		{
			Assert.That(connection.Playlists!.Select(p => p.Id), Is.EqualTo(_afterDelete));
			Assert.That(_api.PlaylistCalls, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task A_failed_playlist_read_leaves_the_cache_unset()
	{
		_api.PlaylistException = FakeYtmDesktopApiClient.RateLimited();
		var connection = Create();

		connection.Start();
		await WaitForAsync(() => connection.IsSessionLive);

		Assert.That(connection.Playlists, Is.Null);
	}

	[Test]
	public async Task Losing_the_socket_resets_the_snapshot_and_reconnects()
	{
		_api.StateJson = PlayingState;
		var connection = Create();

		connection.Start();
		await WaitForAsync(() => connection.Snapshot.Player.IsConnected);

		_realtime.Drop();
		await WaitForAsync(() => !connection.Snapshot.Player.IsConnected);

		Assert.Multiple(() =>
		{
			Assert.That(connection.Snapshot, Is.EqualTo(YtmDesktopSnapshot.Disconnected));
			Assert.That(connection.NeedsAuthorization, Is.False);
		});

		await WaitForAsync(() => _realtime.ConnectCalls >= 2);
		Assert.That(_realtime.ConnectCalls, Is.GreaterThanOrEqualTo(2));
	}

	[Test]
	public async Task A_rejected_token_stops_the_loop_and_asks_for_a_new_authorization()
	{
		_realtime.ConnectException = new YtmDesktopAuthorizationException("token rejected");
		var connection = Create();

		connection.Start();
		await WaitForAsync(() => connection.NeedsAuthorization);

		var attempts = _realtime.ConnectCalls;
		await Task.Delay(_reconnect * 6);

		Assert.Multiple(() =>
		{
			Assert.That(connection.NeedsAuthorization, Is.True);
			Assert.That(_realtime.ConnectCalls, Is.EqualTo(attempts));
		});
	}

	[Test]
	public async Task The_rating_of_the_playing_track_changing_raises_the_event()
	{
		var publisher = new RecordingEventPublisher();
		var connection = Create(new YtmDesktopEventEmitter(publisher));

		connection.Start();

		await WaitForSeededSessionAsync(connection);

		_realtime.RaiseState(PlayingState);
		_realtime.RaiseState(LikedState);

		Assert.Multiple(() =>
		{
			Assert.That(publisher.Published, Has.Count.EqualTo(1));
			Assert.That(publisher.Published[0].EventId, Is.EqualTo("like-changed"));
			Assert.That(publisher.Published[0].Parameters!["likeStatus"], Is.EqualTo("like"));
			Assert.That(publisher.Published[0].Parameters!["previousLikeStatus"], Is.EqualTo("indifferent"));
			Assert.That(publisher.Published[0].Parameters!["videoId"], Is.EqualTo("abc"));
		});
	}

	[Test]
	public async Task The_first_snapshot_of_a_session_is_a_baseline_not_a_rating_change()
	{
		var publisher = new RecordingEventPublisher();
		_api.StateException = FakeYtmDesktopApiClient.RateLimited();
		var connection = Create(new YtmDesktopEventEmitter(publisher));

		connection.Start();
		await WaitForAsync(() => connection.IsSessionLive);

		_realtime.RaiseState(LikedState);

		Assert.That(publisher.Published, Is.Empty);
	}

	[Test]
	public async Task A_track_change_does_not_raise_a_rating_change()
	{
		var publisher = new RecordingEventPublisher();
		var connection = Create(new YtmDesktopEventEmitter(publisher));

		connection.Start();
		await WaitForSeededSessionAsync(connection);

		_realtime.RaiseState(PlayingState);
		_realtime.RaiseState(OtherTrackLikedState);

		Assert.That(publisher.Published, Is.Empty);
	}

	[Test]
	public async Task Shuffle_is_only_sent_when_the_belief_differs()
	{
		var connection = Create();
		connection.Start();

		await WaitForSeededSessionAsync(connection);

		Assert.Multiple(() =>
		{
			Assert.That(connection.ShouldSendShuffle(false), Is.False, "shuffle already believed off");
			Assert.That(connection.ShouldSendShuffle(true), Is.True);
			Assert.That(connection.ShouldSendShuffle(true), Is.False);
			Assert.That(connection.ShouldSendShuffle(false), Is.True);
		});
	}

	[Test]
	public async Task An_optimistic_volume_shows_up_before_the_next_push()
	{
		_api.StateJson = PlayingState;
		var connection = Create();

		connection.Start();
		await WaitForAsync(() => connection.Snapshot.Player.IsConnected);

		connection.ApplyOptimisticVolume(85);

		Assert.That(connection.Snapshot.Player.VolumePercent, Is.EqualTo(85));
	}

	[Test]
	public async Task A_stopped_connection_never_opens_a_session()
	{
		var reached = new ManualResetEventSlim(false);
		var release = new ManualResetEventSlim(false);

		var connection = new YtmDesktopConnection(() =>
			{
				reached.Set();
				release.Wait(TimeSpan.FromSeconds(5));
				return _realtime;
			},
			() => _api,
			new YtmDesktopEndpoint("127.0.0.1", 9863),
			"token",
			reconnectDelay: _reconnect,
			stateRefreshInterval: _refresh,
			commandInterval: TimeSpan.FromMilliseconds(10));

		// Registered and released unconditionally: a test about a pump outliving its connection must not
		// leave one parked for the next test to trip over if an assertion below fails first.
		_connections.Add(connection);

		try
		{
			connection.Start();
			Assert.That(reached.Wait(TimeSpan.FromSeconds(5)), Is.True, "the pump never started a session");

			var stop = connection.StopAsync();
			release.Set();
			await stop;

			var next = new FakeYtmDesktopApiClient();
			_api = next;
			await Task.Delay(_refresh * 4);

			Assert.Multiple(() =>
			{
				Assert.That(next.StateCalls, Is.Zero);
				Assert.That(next.UsedToken, Is.Null);
			});
		}
		finally
		{
			release.Set();
		}
	}

	private YtmDesktopConnection Create(YtmDesktopEventEmitter? events = null)
	{
		var connection = new YtmDesktopConnection(() => _realtime,
			() => _api,
			new YtmDesktopEndpoint("127.0.0.1", 9863),
			"token",
			events,
			_reconnect,
			_refresh,
			TimeSpan.FromMilliseconds(10));

		_connections.Add(connection);
		return connection;
	}

	private static Task WaitForSeededSessionAsync(YtmDesktopConnection connection)
		=> WaitForAsync(() => connection.Snapshot.Player.IsConnected);

	private static async Task WaitForAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
		while (DateTime.UtcNow < deadline)
		{
			if (condition())
			{
				return;
			}

			await Task.Delay(10);
		}

		Assert.Fail("The connection did not reach the expected state in time.");
	}
}
