using System.Net;
using MacroDeckHost.Integrations.Spotify;
using MacroDeckHost.Tests.UnitTests.Auth;
using Serilog;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Http;

namespace MacroDeckHost.Tests.UnitTests.Spotify;

[TestFixture]
internal sealed class SpotifySavedTrackTests
{
	private const string PlayingTrackJson =
		"""
		{
		  "device": { "id": "dev-1", "is_active": true, "name": "Desk", "type": "Computer", "volume_percent": 55 },
		  "repeat_state": "off",
		  "shuffle_state": false,
		  "progress_ms": 1000,
		  "is_playing": true,
		  "currently_playing_type": "track",
		  "item": { "type": "track", "id": "track-1", "uri": "spotify:track:track-1", "name": "Track One",
		    "duration_ms": 200000, "artists": [], "album": { "name": "Album", "images": [] } }
		}
		""";

	private const string PlayingSecondTrackJson =
		"""
		{
		  "device": { "id": "dev-1", "is_active": true, "name": "Desk", "type": "Computer", "volume_percent": 55 },
		  "repeat_state": "off",
		  "shuffle_state": false,
		  "progress_ms": 1000,
		  "is_playing": true,
		  "currently_playing_type": "track",
		  "item": { "type": "track", "id": "track-2", "uri": "spotify:track:track-2", "name": "Track Two",
		    "duration_ms": 200000, "artists": [], "album": { "name": "Album", "images": [] } }
		}
		""";

	private const string PlayingEpisodeJson =
		"""
		{
		  "device": { "id": "dev-1", "is_active": true, "name": "Desk", "type": "Computer", "volume_percent": 55 },
		  "repeat_state": "off",
		  "shuffle_state": false,
		  "progress_ms": 1000,
		  "is_playing": true,
		  "currently_playing_type": "episode",
		  "item": { "type": "episode", "id": "episode-1", "uri": "spotify:episode:episode-1", "name": "Episode One",
		    "duration_ms": 200000, "show": { "name": "Show" }, "images": [] }
		}
		""";

	[Test]
	public async Task IsCurrentItemSavedAsync_returns_null_when_nothing_is_playing()
	{
		var http = new StubSpotifyHttpClient { Handler = _ => Json(HttpStatusCode.NoContent, string.Empty) };
		var player = CreatePlayer(http);
		await player.GetStateAsync();

		Assert.That(await player.IsCurrentItemSavedAsync(), Is.Null);
	}

	[Test]
	public async Task IsCurrentItemSavedAsync_makes_one_library_call_per_track()
	{
		var http = new StubSpotifyHttpClient
		{
			Handler = request => IsPlaybackRequest(request)
				? Json(HttpStatusCode.OK, PlayingTrackJson)
				: Json(HttpStatusCode.OK, "[true]")
		};
		var player = CreatePlayer(http);
		await player.GetStateAsync();

		var first = await player.IsCurrentItemSavedAsync();
		var second = await player.IsCurrentItemSavedAsync();

		Assert.Multiple(() =>
		{
			Assert.That(first, Is.True);
			Assert.That(second, Is.True);
			Assert.That(http.Requests.Count(IsCheckRequest), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task IsCurrentItemSavedAsync_check_carries_the_current_track_uri()
	{
		var http = new StubSpotifyHttpClient
		{
			Handler = request => IsPlaybackRequest(request)
				? Json(HttpStatusCode.OK, PlayingTrackJson)
				: Json(HttpStatusCode.OK, "[false]")
		};
		var player = CreatePlayer(http);
		await player.GetStateAsync();

		var result = await player.IsCurrentItemSavedAsync();

		var check = http.Requests.Single(IsCheckRequest);
		Assert.Multiple(() =>
		{
			Assert.That(result, Is.False);
			Assert.That(check.Method, Is.EqualTo(HttpMethod.Get));
			Assert.That(check.Parameters["uris"], Is.EqualTo("spotify:track:track-1"));
		});
	}

	[Test]
	public async Task IsCurrentItemSavedAsync_works_for_an_episode()
	{
		var http = new StubSpotifyHttpClient
		{
			Handler = request => IsPlaybackRequest(request)
				? Json(HttpStatusCode.OK, PlayingEpisodeJson)
				: Json(HttpStatusCode.OK, "[true]")
		};
		var player = CreatePlayer(http);
		await player.GetStateAsync();

		var result = await player.IsCurrentItemSavedAsync();

		var check = http.Requests.Single(IsCheckRequest);
		Assert.Multiple(() =>
		{
			Assert.That(result, Is.True);
			Assert.That(check.Parameters["uris"], Is.EqualTo("spotify:episode:episode-1"));
		});
	}

	[Test]
	public async Task IsCurrentItemSavedAsync_refetches_after_a_track_change()
	{
		var trackJson = PlayingTrackJson;
		var http = new StubSpotifyHttpClient
		{
			Handler = request => IsPlaybackRequest(request)
				? Json(HttpStatusCode.OK, trackJson)
				: Json(HttpStatusCode.OK, "[true]")
		};
		var time = new ManualTimeProvider();
		var player = CreatePlayer(http, time);
		await player.GetStateAsync();
		await player.IsCurrentItemSavedAsync();

		trackJson = PlayingSecondTrackJson;
		time.Advance(SpotifyPollSchedule.PausedInterval);
		await player.GetStateAsync();
		await player.IsCurrentItemSavedAsync();

		Assert.That(http.Requests.Count(IsCheckRequest), Is.EqualTo(2));
	}

	[Test]
	public async Task IsCurrentItemSavedAsync_latches_off_after_a_403_and_never_throws_again()
	{
		var http = new StubSpotifyHttpClient
		{
			Handler = request => IsPlaybackRequest(request)
				? Json(HttpStatusCode.OK, PlayingTrackJson)
				: Json(HttpStatusCode.Forbidden, """{"error":{"status":403,"message":"Forbidden"}}""")
		};
		var player = CreatePlayer(http);
		await player.GetStateAsync();

		bool? first = null;
		Assert.DoesNotThrowAsync(async () => first = await player.IsCurrentItemSavedAsync());
		var checksAfterFirst = http.Requests.Count(IsCheckRequest);

		player.ForgetSavedState();
		var second = await player.IsCurrentItemSavedAsync();

		Assert.Multiple(() =>
		{
			Assert.That(first, Is.Null);
			Assert.That(second, Is.Null);
			Assert.That(http.Requests.Count(IsCheckRequest), Is.EqualTo(checksAfterFirst));
		});
	}

	[Test]
	public async Task IsCurrentItemSavedAsync_does_not_throw_on_a_500()
	{
		var http = new StubSpotifyHttpClient
		{
			Handler = request => IsPlaybackRequest(request)
				? Json(HttpStatusCode.OK, PlayingTrackJson)
				: Json(HttpStatusCode.InternalServerError, string.Empty)
		};
		var player = CreatePlayer(http);
		await player.GetStateAsync();

		bool? result = null;
		Assert.DoesNotThrowAsync(async () => result = await player.IsCurrentItemSavedAsync());
		Assert.That(result, Is.Null);
	}

	[Test]
	public async Task ForgetSavedState_forces_a_fresh_check_for_the_same_track()
	{
		var http = new StubSpotifyHttpClient
		{
			Handler = request => IsPlaybackRequest(request)
				? Json(HttpStatusCode.OK, PlayingTrackJson)
				: Json(HttpStatusCode.OK, "[true]")
		};
		var player = CreatePlayer(http);
		await player.GetStateAsync();
		await player.IsCurrentItemSavedAsync();

		player.ForgetSavedState();
		await player.IsCurrentItemSavedAsync();

		Assert.That(http.Requests.Count(IsCheckRequest), Is.EqualTo(2));
	}

	private static bool IsPlaybackRequest(IRequest request)
		=> request.Endpoint.ToString().Contains("me/player", StringComparison.Ordinal) &&
			!request.Endpoint.ToString().Contains("devices", StringComparison.Ordinal);

	private static bool IsCheckRequest(IRequest request)
		=> request.Endpoint.ToString().Contains("contains", StringComparison.Ordinal);

	private static Response Json(HttpStatusCode statusCode, string body)
		=> new(new Dictionary<string, string>())
		{
			StatusCode = statusCode,
			ContentType = "application/json",
			Body = body
		};

	private static SpotifyMusicPlayer CreatePlayer(StubSpotifyHttpClient http, TimeProvider? timeProvider = null)
	{
		var tokens = new SpotifyTokenManager(Guid.NewGuid(),
			"client-id",
			"client-secret",
			SpotifyTokenSnapshot.FromStored("token",
				"stored-refresh",
				DateTime.UtcNow.AddHours(1),
				SpotifyTokenManager.RefreshMargin),
			new FakeSpotifyOAuthClient(),
			new SpotifyTokenPersister(new RecordingIntegrationConfig(), new LoggerConfiguration().CreateLogger()),
			new LoggerConfiguration().CreateLogger());
		var player = new SpotifyMusicPlayer(timeProvider: timeProvider);
		player.Connect(SpotifyClientConfig.CreateDefault()
				.WithAuthenticator(new SpotifyAccessTokenAuthenticator(tokens))
				.WithHTTPClient(http),
			tokens);
		return player;
	}
}
