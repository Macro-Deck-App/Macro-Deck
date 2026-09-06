using System.Net;
using MacroDeckHost.Integrations.Spotify;
using Serilog;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Http;

namespace MacroDeckHost.Tests.UnitTests.Spotify;

[TestFixture]
internal sealed class SpotifyTopItemsTests
{
	private const string TopTracksJson =
		"""
		{
		  "items": [ { "id": "t1", "name": "Track One" }, { "id": "t2", "name": "Track Two" } ],
		  "limit": 5, "offset": 0, "total": 2, "href": "https://api.spotify.com/v1/me/top/tracks"
		}
		""";

	private const string TopArtistsJson =
		"""
		{
		  "items": [ { "id": "a1", "name": "Artist One" }, { "id": "a2", "name": "Artist Two" } ],
		  "limit": 5, "offset": 0, "total": 2, "href": "https://api.spotify.com/v1/me/top/artists"
		}
		""";

	private const string EmptyPageJson =
		"""{ "items": [], "limit": 5, "offset": 0, "total": 0, "href": "https://api.spotify.com/v1/me/top/tracks" }""";

	private static readonly string[] _expectedTopTracks = ["Track One", "Track Two"];
	private static readonly string[] _expectedTopArtists = ["Artist One", "Artist Two"];

	[Test]
	public async Task GetTopItems_is_null_before_the_first_fetch_completes()
	{
		// A deliberately slow refresh so the snapshot is still unset the instant GetTopItems returns.
		var oauth = new FakeSpotifyOAuthClient { BeforeRefresh = () => Task.Delay(TimeSpan.FromMilliseconds(50)) };
		var http = new StubSpotifyHttpClient { Handler = TopItemsHandler(TopTracksJson, TopArtistsJson) };
		var player = CreatePlayerWithExpiredToken(oauth, http);

		var immediate = player.GetTopItems();

		Assert.That(immediate, Is.Null);
		Assert.That(await WaitForSnapshotAsync(player), Is.Not.Null);
	}

	[Test]
	public async Task GetTopItems_publishes_the_snapshot_after_the_fetch()
	{
		var http = new StubSpotifyHttpClient { Handler = TopItemsHandler(TopTracksJson, TopArtistsJson) };
		var player = CreatePlayer(http);

		var snapshot = await WaitForSnapshotAsync(player);

		Assert.Multiple(() =>
		{
			Assert.That(snapshot, Is.Not.Null);
			Assert.That(snapshot!.TopTrack, Is.EqualTo("Track One"));
			Assert.That(snapshot.TopArtist, Is.EqualTo("Artist One"));
			Assert.That(snapshot.TopTracks, Is.EqualTo(_expectedTopTracks));
			Assert.That(snapshot.TopArtists, Is.EqualTo(_expectedTopArtists));
		});
	}

	[Test]
	public async Task GetTopItems_requests_the_long_term_range_and_a_limit_of_five()
	{
		var http = new StubSpotifyHttpClient { Handler = TopItemsHandler(TopTracksJson, TopArtistsJson) };
		var player = CreatePlayer(http);

		await WaitForSnapshotAsync(player);

		var request = http.Requests.First(IsTopTracksRequest);
		Assert.Multiple(() =>
		{
			Assert.That(request.Parameters["time_range"], Is.EqualTo("long_term"));
			Assert.That(request.Parameters["limit"], Is.EqualTo("5"));
		});
	}

	[Test]
	public async Task GetTopItems_shares_one_fetch_across_concurrent_callers()
	{
		var oauth = new FakeSpotifyOAuthClient { BeforeRefresh = () => Task.Delay(TimeSpan.FromMilliseconds(50)) };
		var http = new StubSpotifyHttpClient { Handler = TopItemsHandler(TopTracksJson, TopArtistsJson) };
		var player = CreatePlayerWithExpiredToken(oauth, http);

		_ = player.GetTopItems();
		_ = player.GetTopItems();
		_ = player.GetTopItems();
		_ = player.GetTopItems();

		await WaitForSnapshotAsync(player);

		Assert.Multiple(() =>
		{
			Assert.That(oauth.RefreshCount, Is.EqualTo(1));
			Assert.That(http.Requests.Count(IsTopTracksRequest), Is.EqualTo(1));
			Assert.That(http.Requests.Count(IsTopArtistsRequest), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task GetTopItems_stops_after_a_403()
	{
		var http = new StubSpotifyHttpClient
		{
			Handler = request => IsTopTracksRequest(request) || IsTopArtistsRequest(request)
				? Json(HttpStatusCode.Forbidden, """{"error":{"status":403,"message":"Forbidden"}}""")
				: Json(HttpStatusCode.OK, string.Empty)
		};
		var player = CreatePlayer(http);

		Assert.That(await WaitForSnapshotAsync(player), Is.Null);
		var requestsAfterFirstAttempt = http.Requests.Count;

		player.GetTopItems();
		await Task.Delay(TimeSpan.FromMilliseconds(50));

		Assert.Multiple(() =>
		{
			Assert.That(player.GetTopItems(), Is.Null);
			Assert.That(http.Requests.Count, Is.EqualTo(requestsAfterFirstAttempt));
		});
	}

	[Test]
	public async Task GetTopItems_reports_empty_lists_as_unavailable()
	{
		var http = new StubSpotifyHttpClient { Handler = TopItemsHandler(EmptyPageJson, EmptyPageJson) };
		var player = CreatePlayer(http);

		var snapshot = await WaitForSnapshotAsync(player);

		Assert.Multiple(() =>
		{
			Assert.That(snapshot, Is.Not.Null);
			Assert.That(snapshot!.TopTrack, Is.Null);
			Assert.That(snapshot.TopArtist, Is.Null);
			Assert.That(snapshot.TopTracks, Is.Empty);
			Assert.That(snapshot.TopArtists, Is.Empty);
		});
	}

	private static Func<IRequest, IResponse> TopItemsHandler(string tracksJson, string artistsJson)
		=> request => Json(HttpStatusCode.OK, IsTopArtistsRequest(request) ? artistsJson : tracksJson);

	private static bool IsTopTracksRequest(IRequest request)
		=> request.Endpoint.ToString().Contains("top/tracks", StringComparison.Ordinal);

	private static bool IsTopArtistsRequest(IRequest request)
		=> request.Endpoint.ToString().Contains("top/artists", StringComparison.Ordinal);

	private static async Task<SpotifyTopItems?> WaitForSnapshotAsync(SpotifyMusicPlayer player)
	{
		for (var attempt = 0; attempt < 50; attempt++)
		{
			var snapshot = player.GetTopItems();
			if (snapshot is not null)
			{
				return snapshot;
			}

			await Task.Delay(TimeSpan.FromMilliseconds(10));
		}

		return player.GetTopItems();
	}

	private static Response Json(HttpStatusCode statusCode, string body)
		=> new(new Dictionary<string, string>())
		{
			StatusCode = statusCode,
			ContentType = "application/json",
			Body = body
		};

	private static SpotifyMusicPlayer CreatePlayer(StubSpotifyHttpClient http)
		=> CreatePlayer(new FakeSpotifyOAuthClient(), http, TimeSpan.FromHours(1));

	private static SpotifyMusicPlayer CreatePlayerWithExpiredToken(
		FakeSpotifyOAuthClient oauth,
		StubSpotifyHttpClient http)
		=> CreatePlayer(oauth, http, TimeSpan.Zero);

	private static SpotifyMusicPlayer CreatePlayer(
		FakeSpotifyOAuthClient oauth,
		StubSpotifyHttpClient http,
		TimeSpan expiresIn)
	{
		var tokens = new SpotifyTokenManager(Guid.NewGuid(),
			"client-id",
			"client-secret",
			SpotifyTokenSnapshot.FromStored("token",
				"stored-refresh",
				DateTime.UtcNow + expiresIn,
				SpotifyTokenManager.RefreshMargin),
			oauth,
			new SpotifyTokenPersister(new RecordingIntegrationConfig(), new LoggerConfiguration().CreateLogger()),
			new LoggerConfiguration().CreateLogger());
		var player = new SpotifyMusicPlayer();
		player.Connect(SpotifyClientConfig.CreateDefault()
				.WithAuthenticator(new SpotifyAccessTokenAuthenticator(tokens))
				.WithHTTPClient(http),
			tokens);
		return player;
	}
}
