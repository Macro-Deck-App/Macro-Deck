using System.Net;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeckHost.Integrations.Spotify;
using MacroDeckHost.Tests.UnitTests.Auth;
using Serilog;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Http;

namespace MacroDeckHost.Tests.UnitTests.Spotify;

[TestFixture]
internal sealed class SpotifyMusicPlayerPositionTests
{
	[TestCase(true, 3)]
	[TestCase(false, 5)]
	public async Task Autonomous_poller_uses_the_playback_cadence(bool playing, int intervalSeconds)
	{
		var time = new ManualTimeProvider();
		var start = time.Now;
		var reads = new List<TimeSpan>();
		var http = PlaybackTransport(time, start, reads, playing ? Playing() : Paused());
		var (player, tokens) = CreatePlayer(http, time);

		try
		{
			await WaitForPollAsync(time, 0);
			await AdvanceAsync(time, TimeSpan.FromSeconds(intervalSeconds - 1));
			Assert.That(reads.Select(value => value.TotalSeconds), Is.EqualTo(new double[] { 0 }));

			await AdvanceAsync(time, TimeSpan.FromSeconds(1));
			Assert.That(reads.Select(value => value.TotalSeconds),
				Is.EqualTo(new double[] { 0, intervalSeconds }));
		}
		finally
		{
			await player.DisconnectAsync();
			player.Dispose();
			tokens.Dispose();
		}
	}

	[Test]
	public async Task Autonomous_poller_uses_the_idle_cadence()
	{
		var time = new ManualTimeProvider();
		var start = time.Now;
		var reads = new List<TimeSpan>();
		var http = new StubSpotifyHttpClient
		{
			Handler = request =>
			{
				if (IsPlaybackRead(request))
				{
					reads.Add(time.Now - start);
					return Json(HttpStatusCode.NoContent, string.Empty);
				}

				return Json(HttpStatusCode.OK, """{"devices":[]}""");
			}
		};
		var (player, tokens) = CreatePlayer(http, time);

		try
		{
			await WaitForPollAsync(time, 0);
			await AdvanceAsync(time, TimeSpan.FromSeconds(9));
			Assert.That(reads.Select(value => value.TotalSeconds), Is.EqualTo(new double[] { 0 }));

			await AdvanceAsync(time, TimeSpan.FromSeconds(1));
			Assert.That(reads.Select(value => value.TotalSeconds), Is.EqualTo(new double[] { 0, 10 }));
		}
		finally
		{
			await player.DisconnectAsync();
			player.Dispose();
			tokens.Dispose();
		}
	}

	[Test]
	public async Task A_playback_action_polls_immediately_then_three_times_at_750_milliseconds()
	{
		var time = new ManualTimeProvider();
		var start = time.Now;
		var reads = new List<TimeSpan>();
		var http = PlaybackTransport(time, start, reads, Playing());
		var (player, tokens) = CreatePlayer(http, time);

		try
		{
			await WaitForPollAsync(time, 0);
			reads.Clear();
			var scheduled = time.ScheduledCount;
			await player.NextAsync();
			await WaitForPollAsync(time, scheduled);
			for (var i = 0; i < 3; i++)
			{
				await AdvanceAsync(time, TimeSpan.FromMilliseconds(750));
			}

			Assert.That(reads.Select(value => value.TotalMilliseconds),
				Is.EqualTo(new double[] { 0, 750, 1_500, 2_250 }));

			await AdvanceAsync(time, TimeSpan.FromMilliseconds(750));
			Assert.That(reads, Has.Count.EqualTo(4), "there must not be a fourth 750 ms follow-up");
			await AdvanceAsync(time, TimeSpan.FromMilliseconds(2_249));
			Assert.That(reads, Has.Count.EqualTo(4));
			await AdvanceAsync(time, TimeSpan.FromMilliseconds(1));
			Assert.That(reads[4],
				Is.EqualTo(TimeSpan.FromMilliseconds(5_250)),
				"the baseline cadence restarts after the third follow-up");
		}
		finally
		{
			await player.DisconnectAsync();
			player.Dispose();
			tokens.Dispose();
		}
	}

	[TestCase("play")]
	[TestCase("pause")]
	[TestCase("toggle")]
	[TestCase("next")]
	[TestCase("previous")]
	[TestCase("seek")]
	[TestCase("volume")]
	[TestCase("shuffle")]
	[TestCase("repeat")]
	[TestCase("track")]
	[TestCase("playlist")]
	[TestCase("transfer")]
	[TestCase("transfer-and-play")]
	public async Task Every_playback_mutation_requests_an_immediate_poll(string action)
	{
		var time = new ManualTimeProvider();
		var reads = new List<TimeSpan>();
		var http = PlaybackTransport(time, time.Now, reads, Playing());
		var (player, tokens) = CreatePlayer(http, time);

		try
		{
			await WaitForPollAsync(time, 0);
			reads.Clear();
			var scheduled = time.ScheduledCount;
			await ExecuteActionAsync(player, action);
			await WaitForPollAsync(time, scheduled);
			Assert.That(reads.Single(), Is.EqualTo(TimeSpan.Zero));
		}
		finally
		{
			await player.DisconnectAsync();
			player.Dispose();
			tokens.Dispose();
		}
	}

	[Test]
	public async Task A_rejected_playback_mutation_still_requests_the_poll_burst()
	{
		var time = new ManualTimeProvider();
		var start = time.Now;
		var reads = new List<TimeSpan>();
		var http = new StubSpotifyHttpClient
		{
			Handler = request =>
			{
				if (IsPlaybackRead(request))
				{
					reads.Add(time.Now - start);
					return Json(HttpStatusCode.OK, Playing());
				}

				return Json(HttpStatusCode.BadRequest,
					"""{"error":{"status":400,"message":"rejected"}}""");
			}
		};
		var (player, tokens) = CreatePlayer(http, time);

		try
		{
			await WaitForPollAsync(time, 0);
			reads.Clear();
			var scheduled = time.ScheduledCount;
			await player.NextAsync();
			await WaitForPollAsync(time, scheduled);
			Assert.That(reads.Single(), Is.EqualTo(TimeSpan.Zero));
		}
		finally
		{
			await player.DisconnectAsync();
			player.Dispose();
			tokens.Dispose();
		}
	}

	[TestCase("liked")]
	[TestCase("playlist-membership")]
	public async Task Library_mutations_do_not_request_a_playback_burst(string action)
	{
		var time = new ManualTimeProvider();
		var reads = new List<TimeSpan>();
		var http = PlaybackTransport(time, time.Now, reads, Playing());
		var (player, tokens) = CreatePlayer(http, time);

		try
		{
			await WaitForPollAsync(time, 0);
			reads.Clear();
			var scheduled = time.ScheduledCount;
			if (action == "liked")
			{
				await player.ToggleLikedAsync("add");
			}
			else
			{
				await player.ChangePlaylistMembershipAsync("playlist-1", "add");
			}

			await SettleAsync();
			Assert.Multiple(() =>
			{
				Assert.That(reads, Is.Empty);
				Assert.That(time.ScheduledCount, Is.EqualTo(scheduled), "the poll loop must not be woken");
			});
		}
		finally
		{
			await player.DisconnectAsync();
			player.Dispose();
			tokens.Dispose();
		}
	}

	[Test]
	public async Task The_latest_action_replaces_the_previous_follow_up_sequence()
	{
		var time = new ManualTimeProvider();
		var start = time.Now;
		var reads = new List<TimeSpan>();
		var http = PlaybackTransport(time, start, reads, Playing());
		var (player, tokens) = CreatePlayer(http, time);

		try
		{
			await WaitForPollAsync(time, 0);
			reads.Clear();
			var scheduled = time.ScheduledCount;
			await player.NextAsync();
			await WaitForPollAsync(time, scheduled);
			await AdvanceAsync(time, TimeSpan.FromMilliseconds(500));
			scheduled = time.ScheduledCount;
			await player.PreviousAsync();
			await WaitForPollAsync(time, scheduled);
			for (var i = 0; i < 3; i++)
			{
				await AdvanceAsync(time, TimeSpan.FromMilliseconds(750));
			}

			Assert.That(reads.Select(value => value.TotalMilliseconds),
				Is.EqualTo(new double[] { 0, 500, 1_250, 2_000, 2_750 }));
		}
		finally
		{
			await player.DisconnectAsync();
			player.Dispose();
			tokens.Dispose();
		}
	}

	[Test]
	public async Task Cached_state_reads_do_not_call_Spotify_and_refresh_state_awaits_one_poll()
	{
		var time = new ManualTimeProvider();
		var reads = new List<TimeSpan>();
		var http = PlaybackTransport(time, time.Now, reads, Playing());
		var (player, tokens) = CreatePlayer(http, time);

		try
		{
			await WaitForPollAsync(time, 0);
			var before = reads.Count;
			await player.GetStateAsync();
			Assert.That(reads, Has.Count.EqualTo(before));

			await player.RefreshStateAsync();
			Assert.That(reads, Has.Count.EqualTo(before + 1));
		}
		finally
		{
			await player.DisconnectAsync();
			player.Dispose();
			tokens.Dispose();
		}
	}

	[Test]
	public async Task A_connected_player_rejects_a_second_poll_generation()
	{
		var time = new ManualTimeProvider();
		var http = PlaybackTransport(time, time.Now, [], Playing());
		var (player, tokens) = CreatePlayer(http, time);

		try
		{
			Assert.Throws<InvalidOperationException>(() => player.Connect(Config(http, tokens), tokens));
		}
		finally
		{
			await player.DisconnectAsync();
			player.Dispose();
			tokens.Dispose();
		}
	}

	[Test]
	public async Task Dispose_waits_for_an_in_flight_poll_and_leaves_no_live_generation()
	{
		var time = new ManualTimeProvider();
		var http = new BlockingPlaybackTransport();
		var (player, tokens) = CreatePlayer(http, time);
		Task? disposing = null;

		try
		{
			await http.Arrived.Task.WaitAsync(HangGuard);
			disposing = Task.Run(player.Dispose);
			await SettleAsync();
			Assert.That(disposing.IsCompleted, Is.False);

			http.Release.TrySetResult(Json(HttpStatusCode.OK, Playing()));
			await disposing.WaitAsync(HangGuard);
			Assert.That(player.LastState, Is.EqualTo(MusicPlayerState.Disconnected));

			await AdvanceAsync(time, TimeSpan.FromSeconds(20));
			Assert.That(http.PlaybackReads, Is.EqualTo(1));
		}
		finally
		{
			http.Release.TrySetResult(Json(HttpStatusCode.OK, Playing()));
			if (disposing is not null)
			{
				await disposing.WaitAsync(HangGuard);
			}
			else
			{
				await player.DisconnectAsync();
			}

			tokens.Dispose();
		}
	}

	[Test]
	public async Task Autonomous_poller_uses_the_track_end_hint_when_it_is_sooner()
	{
		var time = new ManualTimeProvider();
		var start = time.Now;
		var reads = new List<TimeSpan>();
		var http = PlaybackTransport(time, start, reads, Playback(true, 59_000, 60_000));
		var (player, tokens) = CreatePlayer(http, time);

		try
		{
			await WaitForPollAsync(time, 0);
			await AdvanceAsync(time, TimeSpan.FromMilliseconds(1_499));
			Assert.That(reads, Has.Count.EqualTo(1));

			await AdvanceAsync(time, TimeSpan.FromMilliseconds(1));
			Assert.That(reads.Select(value => value.TotalMilliseconds),
				Is.EqualTo(new double[] { 0, 1_500 }));
		}
		finally
		{
			await player.DisconnectAsync();
			player.Dispose();
			tokens.Dispose();
		}
	}

	[Test]
	public async Task A_transient_poll_failure_is_retried_after_three_seconds_without_an_inline_retry()
	{
		var time = new ManualTimeProvider();
		var start = time.Now;
		var reads = new List<TimeSpan>();
		var fail = true;
		var http = new StubSpotifyHttpClient
		{
			Handler = request =>
			{
				if (IsPlaybackRead(request))
				{
					reads.Add(time.Now - start);
					if (fail)
					{
						fail = false;
						return Json(HttpStatusCode.BadGateway, string.Empty);
					}
				}

				return Json(HttpStatusCode.OK, Playing());
			}
		};
		var (player, tokens) = CreatePlayer(http, time);

		try
		{
			await WaitForPollAsync(time, 0);
			Assert.That(reads, Has.Count.EqualTo(1));

			await AdvanceAsync(time, TimeSpan.FromMilliseconds(2_999));
			Assert.That(reads, Has.Count.EqualTo(1));

			await AdvanceAsync(time, TimeSpan.FromMilliseconds(1));
			Assert.That(reads.Select(value => value.TotalSeconds), Is.EqualTo(new double[] { 0, 3 }));
		}
		finally
		{
			await player.DisconnectAsync();
			player.Dispose();
			tokens.Dispose();
		}
	}

	[Test]
	public async Task Position_advances_between_polls_without_calling_the_api()
	{
		var time = new ManualTimeProvider();
		var reads = new List<TimeSpan>();
		var http = PlaybackTransport(time, time.Now, reads, Playback(true, 30_000, 200_000));
		var (player, tokens) = CreatePlayer(http, time, autonomousPolling: false);

		try
		{
			await player.GetStateAsync();
			var requestsBefore = http.Requests.Count;
			var positions = new List<int?>();
			for (var i = 0; i < 4; i++)
			{
				time.Advance(TimeSpan.FromSeconds(1));
				positions.Add(player.CurrentState.Position is { } position ? (int)position.TotalSeconds : null);
			}

			Assert.Multiple(() =>
			{
				Assert.That(positions, Is.EqualTo(new int?[] { 31, 32, 33, 34 }));
				Assert.That(http.Requests, Has.Count.EqualTo(requestsBefore));
			});
		}
		finally
		{
			await player.DisconnectAsync();
			player.Dispose();
			tokens.Dispose();
		}
	}

	[Test]
	public async Task Position_does_not_advance_while_paused()
	{
		var time = new ManualTimeProvider();
		var reads = new List<TimeSpan>();
		var http = PlaybackTransport(time, time.Now, reads, Playback(false, 42_000, 180_000));
		var (player, tokens) = CreatePlayer(http, time, autonomousPolling: false);

		try
		{
			await player.GetStateAsync();
			time.Advance(TimeSpan.FromSeconds(4));
			Assert.That(player.CurrentState.Position, Is.EqualTo(TimeSpan.FromSeconds(42)));
		}
		finally
		{
			await player.DisconnectAsync();
			player.Dispose();
			tokens.Dispose();
		}
	}

	[Test]
	public async Task Position_is_clamped_to_the_track_duration()
	{
		var time = new ManualTimeProvider();
		var reads = new List<TimeSpan>();
		var http = PlaybackTransport(time, time.Now, reads, Playback(true, 196_000, 200_000));
		var (player, tokens) = CreatePlayer(http, time, autonomousPolling: false);

		try
		{
			await player.GetStateAsync();
			time.Advance(TimeSpan.FromSeconds(5));
			Assert.That(player.CurrentState.Position, Is.EqualTo(TimeSpan.FromSeconds(200)));
		}
		finally
		{
			await player.DisconnectAsync();
			player.Dispose();
			tokens.Dispose();
		}
	}

	private static StubSpotifyHttpClient PlaybackTransport(
		ManualTimeProvider time,
		DateTimeOffset start,
		List<TimeSpan> reads,
		string playback)
		=> new()
		{
			Handler = request =>
			{
				if (IsPlaybackRead(request))
				{
					reads.Add(time.Now - start);
					return Json(HttpStatusCode.OK, playback);
				}

				return Json(HttpStatusCode.NoContent, string.Empty);
			}
		};

	private static (SpotifyMusicPlayer Player, SpotifyTokenManager Tokens) CreatePlayer(
		IHTTPClient http,
		TimeProvider timeProvider,
		bool autonomousPolling = true)
	{
		var tokens = new SpotifyTokenManager(Guid.NewGuid(),
			"client-id",
			"client-secret",
			SpotifyTokenSnapshot.FromStored("token",
				"stored-refresh",
				DateTime.UtcNow + TimeSpan.FromHours(1),
				SpotifyTokenManager.RefreshMargin),
			new FakeSpotifyOAuthClient(),
			new SpotifyTokenPersister(new RecordingIntegrationConfig(), new LoggerConfiguration().CreateLogger()),
			new LoggerConfiguration().CreateLogger());
		var player = new SpotifyMusicPlayer(timeProvider: timeProvider, autonomousPolling: autonomousPolling);
		player.Connect(Config(http, tokens), tokens);
		return (player, tokens);
	}

	private static SpotifyClientConfig Config(IHTTPClient http, ISpotifyAccessTokenSource tokens)
		=> SpotifyClientConfig.CreateDefault()
			.WithAuthenticator(new SpotifyAccessTokenAuthenticator(tokens))
			.WithHTTPClient(http)
			.WithRetryHandler(new SpotifyRetryHandler());

	private static bool IsPlaybackRead(IRequest request)
		=> request.Method == HttpMethod.Get &&
			request.Endpoint.ToString().Contains("me/player", StringComparison.Ordinal) &&
			!request.Endpoint.ToString().Contains("devices", StringComparison.Ordinal);

	// Every poll loop iteration ends by arming a delay on the fake clock, so waiting for the arm count to
	// grow is an exact "the iteration has finished" barrier. The cap only turns a genuine hang into a
	// failing test instead of a stuck run; it is never a budget the loop is expected to fit into.
	private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(10);

	private static async Task WaitForPollAsync(ManualTimeProvider time, long scheduledBefore)
	{
		try
		{
			await time.WaitForScheduleAsync(scheduledBefore).WaitAsync(HangGuard);
		}
		catch (TimeoutException)
		{
			Assert.Fail("the poll loop did not finish another iteration");
		}
	}

	private static async Task AdvanceAsync(ManualTimeProvider time, TimeSpan delta)
	{
		var scheduled = time.ScheduledCount;
		if (time.Advance(delta))
		{
			await WaitForPollAsync(time, scheduled);
		}
	}

	private static async Task SettleAsync()
	{
		for (var i = 0; i < 5; i++)
		{
			await Task.Yield();
		}
	}

	private static Task ExecuteActionAsync(SpotifyMusicPlayer player, string action)
		=> action switch
		{
			"play" => player.PlayAsync(),
			"pause" => player.PauseAsync(),
			"toggle" => player.TogglePlayPauseAsync(),
			"next" => player.NextAsync(),
			"previous" => player.PreviousAsync(),
			"seek" => player.SeekAsync(TimeSpan.FromSeconds(12)),
			"volume" => player.SetVolumeAsync(42),
			"shuffle" => player.SetShuffleAsync(true),
			"repeat" => player.SetRepeatModeAsync(RepeatMode.Context),
			"track" => player.PlayItemAsync(new MusicPlayerCatalogItem("track-2",
				"Track 2",
				MusicPlayerCatalogItemKind.Track)),
			"playlist" => player.PlayItemAsync(new MusicPlayerCatalogItem("playlist-2",
				"Playlist 2",
				MusicPlayerCatalogItemKind.Playlist)),
			"transfer" => player.TransferPlaybackAsync("device-2", false, CancellationToken.None),
			"transfer-and-play" => player.TransferPlaybackAsync("device-2", true, CancellationToken.None),
			_ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
		};

	private static string Playing() => Playback(isPlaying: true);

	private static string Paused() => Playback(isPlaying: false);

	private static string Playback(bool isPlaying, int progressMs = 1_000, int durationMs = 200_000) => $$"""
		  {
		    "device": { "id": "dev-1", "is_active": true, "name": "Desk", "type": "Computer", "volume_percent": 55 },
		    "repeat_state": "off",
		    "shuffle_state": false,
		    "progress_ms": {{progressMs}},
		    "is_playing": {{isPlaying.ToString().ToLowerInvariant()}},
		    "currently_playing_type": "track",
		    "item": {
		      "type": "track",
		      "id": "track-1",
		      "uri": "spotify:track:track-1",
		      "name": "Track",
		      "duration_ms": {{durationMs}},
		      "artists": [{ "name": "Artist" }],
		      "album": { "name": "Album", "images": [] }
		    }
		  }
		  """;

	private static Response Json(HttpStatusCode statusCode, string body)
		=> new(new Dictionary<string, string>())
		{
			StatusCode = statusCode,
			ContentType = "application/json",
			Body = body
		};

	private sealed class BlockingPlaybackTransport : IHTTPClient
	{
		public TaskCompletionSource Arrived { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public TaskCompletionSource<IResponse> Release { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public int PlaybackReads { get; private set; }

		public Task<IResponse> DoRequest(IRequest request, CancellationToken cancel)
		{
			if (!IsPlaybackRead(request))
			{
				return Task.FromResult<IResponse>(Json(HttpStatusCode.NoContent, string.Empty));
			}

			PlaybackReads++;
			Arrived.TrySetResult();
			return Release.Task;
		}

		public void SetRequestTimeout(TimeSpan timeout)
		{
		}

		public void Dispose()
		{
		}
	}
}
