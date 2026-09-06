using System.Net;
using MacroDeckHost.Integrations.YtmDesktop;
using MacroDeckHost.Integrations.YtmDesktop.Protocol;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.MusicPlayer;

namespace MacroDeckHost.Tests.UnitTests.YtmDesktop;

[TestFixture]
internal sealed class YtmDesktopMusicPlayerTests
{
	private static readonly string[] _transportCommands = ["play", "pause", "playPause", "next", "previous"];
	private static readonly string[] _queueIds = ["one", "two"];
	private static readonly string[] _filteredQueueIds = ["two"];

	private const string QueueState = """
									  {
									  	"player": { "trackState": 1, "videoProgress": 12, "volume": 60, "muted": false, "adPlaying": false,
									  		"queue": { "repeatMode": 0, "selectedItemIndex": 0, "automixItems": [],
									  			"items": [
									  				{ "videoId": "one", "title": "First", "author": "A", "duration": "3:00", "selected": true,
									  					"thumbnails": [] },
									  				{ "videoId": "two", "title": "Second", "author": "B", "duration": "4:00", "selected": false,
									  					"thumbnails": [] }
									  			] } },
									  	"video": { "id": "one", "title": "First", "author": "A", "album": null, "likeStatus": 1,
									  		"thumbnails": [], "durationSeconds": 200, "isLive": false, "videoType": 0, "metadataFilled": true },
									  	"playlistId": null
									  }
									  """;

	private FakeYtmDesktopApiClient _api = null!;
	private FakeYtmDesktopRealtimeClient _realtime = null!;
	private YtmDesktopConnection _connection = null!;
	private YtmDesktopMusicPlayer _player = null!;

	[SetUp]
	public async Task SetUp()
	{
		_api = new FakeYtmDesktopApiClient { StateJson = QueueState };
		_realtime = new FakeYtmDesktopRealtimeClient();
		_player = new YtmDesktopMusicPlayer();
		_connection = new YtmDesktopConnection(() => _realtime,
			() => _api,
			new YtmDesktopEndpoint("127.0.0.1", 9863),
			"token",
			reconnectDelay: TimeSpan.FromMilliseconds(30),
			stateRefreshInterval: TimeSpan.FromSeconds(30),
			commandInterval: TimeSpan.FromMilliseconds(10),
			registerArtwork: _player.RegisterArtwork);

		_player.Connect(_connection);
		_connection.Start();
		await WaitForAsync(() => _connection.Snapshot.Player.IsConnected);
	}

	[TearDown]
	public void TearDown()
	{
		_connection.Dispose();
		_api.Dispose();
		_realtime.Dispose();
	}

	[Test]
	public async Task Reading_the_state_never_costs_a_request()
	{
		var before = _api.StateCalls;

		for (var i = 0; i < 10; i++)
		{
			await _player.GetStateAsync();
		}

		Assert.That(_api.StateCalls, Is.EqualTo(before));
	}

	[Test]
	public async Task The_transport_commands_map_to_their_companion_names()
	{
		await _player.PlayAsync();
		await _player.PauseAsync();
		await _player.TogglePlayPauseAsync();
		await _player.NextAsync();
		await _player.PreviousAsync();

		Assert.That(_api.Commands.Select(c => c.Command),
			Is.EqualTo(_transportCommands));
	}

	[Test]
	public async Task Seeking_is_clamped_to_the_track_length()
	{
		await _player.SeekAsync(TimeSpan.FromSeconds(9999));
		await WaitForAsync(() => _api.Commands.Count > 0);

		Assert.Multiple(() =>
		{
			Assert.That(_api.Commands[0].Command, Is.EqualTo("seekTo"));
			Assert.That(_api.Commands[0].Data, Is.EqualTo(200));
		});
	}

	[Test]
	public async Task A_negative_seek_lands_at_the_start()
	{
		await _player.SeekAsync(TimeSpan.FromSeconds(-30));
		await WaitForAsync(() => _api.Commands.Count > 0);

		Assert.That(_api.Commands[0].Data, Is.EqualTo(0));
	}

	[Test]
	public async Task The_volume_is_clamped_and_shows_up_immediately()
	{
		await _player.SetVolumeAsync(150);
		await WaitForAsync(() => _api.Commands.Count > 0);

		Assert.Multiple(() =>
		{
			Assert.That(_api.Commands[0].Command, Is.EqualTo("setVolume"));
			Assert.That(_api.Commands[0].Data, Is.EqualTo(100));

			Assert.That(_connection.Snapshot.Player.VolumePercent, Is.EqualTo(100));
		});
	}

	[TestCase(RepeatMode.Off, 0)]
	[TestCase(RepeatMode.Context, 1)]
	[TestCase(RepeatMode.Track, 2)]
	public async Task The_repeat_mode_is_sent_as_its_companion_number(RepeatMode mode, int expected)
	{
		await _player.SetRepeatModeAsync(mode);

		Assert.Multiple(() =>
		{
			Assert.That(_api.Commands[0].Command, Is.EqualTo("repeatMode"));
			Assert.That(_api.Commands[0].Data, Is.EqualTo(expected));
		});
	}

	[Test]
	public async Task Shuffle_is_only_sent_when_it_would_change_something()
	{
		await _player.SetShuffleAsync(false);
		Assert.That(_api.Commands, Is.Empty);

		await _player.SetShuffleAsync(true);

		Assert.Multiple(() =>
		{
			Assert.That(_api.Commands, Has.Count.EqualTo(1));
			Assert.That(_api.Commands[0].Command, Is.EqualTo("shuffle"));
			Assert.That(_api.Commands[0].Data, Is.Null);
		});
	}

	[Test]
	public async Task Playing_a_queued_track_keeps_the_queue()
	{
		await _player.PlayItemAsync(new MusicPlayerCatalogItem("two", "Second", MusicPlayerCatalogItemKind.Track));

		Assert.Multiple(() =>
		{
			Assert.That(_api.Commands[0].Command, Is.EqualTo("playQueueIndex"));
			Assert.That(_api.Commands[0].Data, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Playing_a_track_that_is_not_queued_changes_the_video()
	{
		await _player.PlayItemAsync(new MusicPlayerCatalogItem("elsewhere", "X", MusicPlayerCatalogItemKind.Track));

		Assert.Multiple(() =>
		{
			Assert.That(_api.Commands[0].Command, Is.EqualTo("changeVideo"));
			Assert.That(_api.Commands[0].Data?.ToString(), Does.Contain("elsewhere"));
		});
	}

	[Test]
	public async Task Playing_a_playlist_changes_the_video_by_playlist_id()
	{
		await _player.PlayItemAsync(new MusicPlayerCatalogItem("PL1", "Mix", MusicPlayerCatalogItemKind.Playlist));

		Assert.Multiple(() =>
		{
			Assert.That(_api.Commands[0].Command, Is.EqualTo("changeVideo"));
			Assert.That(_api.Commands[0].Data?.ToString(), Does.Contain("PL1"));
		});
	}

	[Test]
	public async Task A_rejected_command_does_not_take_the_action_flow_down()
	{
		_api.CommandException = new YtmDesktopApiException("nope", HttpStatusCode.ServiceUnavailable);

		Assert.DoesNotThrowAsync(async () => await _player.NextAsync());
		await Task.CompletedTask;
	}

	[TestCase("like", YtmLikeStatus.Indifferent, "toggleLike")]
	[TestCase("like", YtmLikeStatus.Dislike, "toggleLike")]
	[TestCase("dislike", YtmLikeStatus.Indifferent, "toggleDislike")]
	[TestCase("dislike", YtmLikeStatus.Like, "toggleDislike")]
	[TestCase("clear", YtmLikeStatus.Like, "toggleLike")]
	[TestCase("clear", YtmLikeStatus.Dislike, "toggleDislike")]
	[TestCase("toggle-like", YtmLikeStatus.Like, "toggleLike")]
	[TestCase("toggle-dislike", YtmLikeStatus.Dislike, "toggleDislike")]
	public async Task A_rating_mode_resolves_against_the_current_rating(
		string mode,
		YtmLikeStatus current,
		string expected)
	{
		PushRating(current);

		var result = await _player.SetRatingAsync(mode, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(_api.Commands.Select(c => c.Command), Is.EqualTo(new[] { expected }));
		});
	}

	[TestCase("like", YtmLikeStatus.Like)]
	[TestCase("dislike", YtmLikeStatus.Dislike)]
	[TestCase("clear", YtmLikeStatus.Indifferent)]
	public async Task A_rating_that_is_already_set_sends_nothing(string mode, YtmLikeStatus current)
	{
		PushRating(current);

		var result = await _player.SetRatingAsync(mode, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(_api.Commands, Is.Empty);
		});
	}

	[Test]
	public async Task An_unknown_rating_toggles_for_like_and_does_nothing_for_clear()
	{
		PushRating(YtmLikeStatus.Unknown);

		await _player.SetRatingAsync("like", CancellationToken.None);
		var afterLike = _api.Commands.Count;
		await _player.SetRatingAsync("clear", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(afterLike, Is.EqualTo(1));
			Assert.That(_api.Commands, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task Rating_nothing_fails_rather_than_pretending_to_work()
	{
		_realtime.RaiseState(FakeYtmDesktopApiClient.EmptyState);

		var result = await _player.SetRatingAsync("like", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
		});
	}

	[TestCase("on", "mute")]
	[TestCase("off", "unmute")]
	public async Task Muting_maps_to_its_command(string mode, string expected)
	{
		await _player.SetMuteAsync(mode, CancellationToken.None);

		Assert.That(_api.Commands[0].Command, Is.EqualTo(expected));
	}

	[Test]
	public async Task Toggling_mute_reads_the_reported_state()
	{
		await _player.SetMuteAsync("toggle", CancellationToken.None);

		Assert.That(_api.Commands[0].Command, Is.EqualTo("mute"));
	}

	[Test]
	public async Task An_unusable_link_reports_an_invalid_parameter()
	{
		var result = await _player.PlayVideoOrUrlAsync("   ", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
			Assert.That(_api.Commands, Is.Empty);
		});
	}

	[Test]
	public async Task A_link_starts_the_video_it_names()
	{
		var result = await _player.PlayVideoOrUrlAsync("https://music.youtube.com/watch?v=dQw4w9WgXcQ",
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(_api.Commands[0].Command, Is.EqualTo("changeVideo"));
			Assert.That(_api.Commands[0].Data?.ToString(), Does.Contain("dQw4w9WgXcQ"));
		});
	}

	[Test]
	public async Task The_track_catalog_is_the_current_queue()
	{
		var items = await _player.GetCatalogAsync("entry",
			MusicPlayerCatalogItemKind.Track,
			null,
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(items.Select(i => i.Id), Is.EqualTo(_queueIds));
			Assert.That(items[1].Subtitle, Is.EqualTo("B"));
		});
	}

	[Test]
	public async Task The_track_catalog_filters_on_title_and_author()
	{
		var items = await _player.GetCatalogAsync("entry",
			MusicPlayerCatalogItemKind.Track,
			"second",
			CancellationToken.None);

		Assert.That(items.Select(i => i.Id), Is.EqualTo(_filteredQueueIds));
	}

	[Test]
	public async Task A_playlist_read_that_succeeded_but_found_nothing_returns_an_empty_list()
	{
		var items = await _player.GetCatalogAsync("entry",
			MusicPlayerCatalogItemKind.Playlist,
			null,
			CancellationToken.None);

		Assert.That(items, Is.Empty);
	}

	[Test]
	public void A_disconnected_catalog_read_throws_rather_than_returning_nothing()
	{
		_player.Disconnect();

		Assert.ThatAsync(() =>
				_player.GetCatalogAsync("entry", MusicPlayerCatalogItemKind.Playlist, null, CancellationToken.None),
			Throws.InstanceOf<InvalidOperationException>());
	}

	[Test]
	public async Task An_unknown_artwork_id_has_no_bytes()
	{
		var artwork = await _player.GetArtworkAsync("nope");

		Assert.That(artwork, Is.Null);
	}

	[Test]
	public async Task A_disconnected_player_reports_the_disconnected_state()
	{
		_player.Disconnect();

		var state = await _player.GetStateAsync();

		Assert.That(state, Is.EqualTo(MusicPlayerState.Disconnected));
	}

	[Test]
	public async Task A_closed_app_reports_unavailable_rather_than_not_connected()
	{
		using var realtime = new FakeYtmDesktopRealtimeClient { ConnectException = new IOException("refused") };
		using var api = new FakeYtmDesktopApiClient { StateJson = QueueState };
		var player = new YtmDesktopMusicPlayer();
		using var connection = Connection(realtime, api, player);

		connection.Start();
		await WaitForAsync(() => realtime.ConnectCalls >= 1);
		var state = await player.GetStateAsync();

		Assert.Multiple(() =>
		{
			Assert.That(state.IsConnected, Is.False);
			Assert.That(state.IsUnavailable, Is.True);
			Assert.That(state.StatusMessage, Is.EqualTo("YouTube Music is not running"));
		});
	}

	[Test]
	public async Task A_live_session_without_state_yet_reports_unavailable()
	{
		using var realtime = new FakeYtmDesktopRealtimeClient();
		using var api = new FakeYtmDesktopApiClient { StateException = FakeYtmDesktopApiClient.RateLimited() };
		var player = new YtmDesktopMusicPlayer();
		using var connection = Connection(realtime, api, player);

		connection.Start();
		await WaitForAsync(() => connection.IsSessionLive);
		var state = await player.GetStateAsync();

		Assert.Multiple(() =>
		{
			Assert.That(state.IsUnavailable, Is.True);
			Assert.That(state.StatusMessage, Is.EqualTo("Waiting for YouTube Music"));
		});
	}

	private static YtmDesktopConnection Connection(
		FakeYtmDesktopRealtimeClient realtime,
		FakeYtmDesktopApiClient api,
		YtmDesktopMusicPlayer player)
	{
		var connection = new YtmDesktopConnection(() => realtime,
			() => api,
			new YtmDesktopEndpoint("127.0.0.1", 9863),
			"token",
			reconnectDelay: TimeSpan.FromMilliseconds(30),
			stateRefreshInterval: TimeSpan.FromSeconds(30),
			commandInterval: TimeSpan.FromMilliseconds(10),
			registerArtwork: player.RegisterArtwork);
		player.Connect(connection);
		return connection;
	}

	private void PushRating(YtmLikeStatus status)
	{
		var json = QueueState.Replace("\"likeStatus\": 1",
			$"\"likeStatus\": {(int)status}",
			StringComparison.Ordinal);

		_realtime.RaiseState(json);
	}

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

		Assert.Fail("The player did not reach the expected state in time.");
	}
}
