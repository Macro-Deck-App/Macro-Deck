using System.Globalization;
using System.Net;
using System.Text.Json;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeckHost.Integrations.Spotify;
using Serilog;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Http;

namespace MacroDeckHost.Tests.UnitTests.Spotify;

[TestFixture]
internal sealed class SpotifyMusicPlayerCatalogTests
{
	[Test]
	public void MapTracks_SkipsNullAndIdLessEntries()
	{
		var player = new SpotifyMusicPlayer();
		var valid = new FullTrack
		{
			Id = "track-1",
			Name = "Track One",
			Artists = [new SimpleArtist { Name = "Artist" }],
			Album = new SimpleAlbum { Images = [] },
			DurationMs = 120000
		};
		var withoutId = new FullTrack
		{
			Name = "No Id",
			Artists = [],
			Album = new SimpleAlbum { Images = [] }
		};

		var items = player.MapTracks([null, valid, withoutId]);

		Assert.That(items, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(items[0].Id, Is.EqualTo("track-1"));
			Assert.That(items[0].Title, Is.EqualTo("Track One"));
			Assert.That(items[0].Subtitle, Is.EqualTo("Artist"));
			Assert.That(items[0].Duration, Is.EqualTo(TimeSpan.FromMinutes(2)));
		});
	}

	[Test]
	public void MapPlaylists_SkipsNullAndIdLessEntries()
	{
		var player = new SpotifyMusicPlayer();
		var valid = new FullPlaylist
		{
			Id = "playlist-1",
			Name = "Playlist One",
			Owner = new PublicUser { DisplayName = "Owner" }
		};
		var withoutId = new FullPlaylist { Name = "No Id" };

		var items = player.MapPlaylists([null, valid, withoutId]);

		Assert.That(items, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(items[0].Id, Is.EqualTo("playlist-1"));
			Assert.That(items[0].Title, Is.EqualTo("Playlist One"));
			Assert.That(items[0].Subtitle, Is.EqualTo("Owner"));
		});
	}

	[Test]
	public async Task GetCatalogAsync_offers_liked_songs_beyond_the_first_page()
	{
		var spotify = new FakeSpotifyCatalog { LikedSongs = 120 };
		var player = CreatePlayer(spotify);

		var items = await player.GetCatalogAsync("entry", MusicPlayerCatalogItemKind.Track, null, CancellationToken.None);

		Assert.That(items.Select(i => i.Id).ToList(), Is.EqualTo(Enumerable.Range(0, 120).Select(n => $"liked-{n}").ToList()));
	}

	[Test]
	public async Task GetCatalogAsync_offers_own_playlists_beyond_the_first_page()
	{
		var spotify = new FakeSpotifyCatalog { OwnPlaylists = 75 };
		var player = CreatePlayer(spotify);

		var items = await player.GetCatalogAsync("entry",
			MusicPlayerCatalogItemKind.Playlist,
			null,
			CancellationToken.None);

		Assert.That(items.Select(i => i.Id).ToList(), Is.EqualTo(Enumerable.Range(0, 75).Select(n => $"playlist-{n}").ToList()));
	}

	[TestCase(MusicPlayerCatalogItemKind.Track)]
	[TestCase(MusicPlayerCatalogItemKind.Playlist)]
	public async Task GetCatalogAsync_search_offers_more_than_spotifys_default_of_five(MusicPlayerCatalogItemKind kind)
	{
		var spotify = new FakeSpotifyCatalog();
		var player = CreatePlayer(spotify);

		var items = await player.GetCatalogAsync("entry", kind, "love", CancellationToken.None);

		Assert.That(items, Has.Count.GreaterThan(FakeSpotifyCatalog.SearchDefaultLimit));
	}

	[Test]
	public async Task GetCatalogAsync_offers_a_bounded_prefix_of_a_huge_library_with_few_requests()
	{
		var spotify = new FakeSpotifyCatalog { LikedSongs = 5000 };
		var player = CreatePlayer(spotify);

		var items = await player.GetCatalogAsync("entry", MusicPlayerCatalogItemKind.Track, null, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(items, Has.Count.GreaterThan(50));
			Assert.That(items.Select(i => i.Id).ToList(),
				Is.EqualTo(Enumerable.Range(0, items.Count).Select(n => $"liked-{n}").ToList()));
			Assert.That(spotify.CatalogRequests.Count(), Is.LessThan(10));
		});
	}

	[Test]
	public async Task GetCatalogAsync_stops_after_the_last_page_of_the_library()
	{
		var spotify = new FakeSpotifyCatalog { LikedSongs = 100 };
		var player = CreatePlayer(spotify);

		var items = await player.GetCatalogAsync("entry", MusicPlayerCatalogItemKind.Track, null, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(items, Has.Count.EqualTo(100));
			Assert.That(spotify.CatalogRequests.Select(FakeSpotifyCatalog.Offset), Has.All.LessThan(100));
		});
	}

	[Test]
	public async Task GetCatalogAsync_offers_each_song_once_when_one_is_liked_while_reading()
	{
		var spotify = new FakeSpotifyCatalog { LikedSongs = 120, LikedWhileReading = true };
		var player = CreatePlayer(spotify);

		var items = await player.GetCatalogAsync("entry", MusicPlayerCatalogItemKind.Track, null, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(items.Select(i => i.Id), Is.Unique);
			Assert.That(items.Select(i => i.Id), Is.SupersetOf(Enumerable.Range(0, 120).Select(n => $"liked-{n}")));
		});
	}

	[Test]
	public async Task GetCatalogAsync_rate_limit_on_a_later_page_fails_the_read_and_pauses_further_reads()
	{
		var spotify = new FakeSpotifyCatalog { LikedSongs = 120, RateLimitedFromRequest = 2 };
		var player = CreatePlayer(spotify);

		Assert.That(async () => await player.GetCatalogAsync("entry",
				MusicPlayerCatalogItemKind.Track,
				null,
				CancellationToken.None),
			Throws.Exception);
		var requestsAfterRefusal = spotify.CatalogRequests.Count();

		Assert.Multiple(() =>
		{
			Assert.That(async () => await player.GetCatalogAsync("entry",
					MusicPlayerCatalogItemKind.Track,
					null,
					CancellationToken.None),
				Throws.Exception);
			Assert.That(spotify.CatalogRequests.Count(), Is.EqualTo(requestsAfterRefusal));
			Assert.That(player.ApiLimit?.Kind, Is.EqualTo(SpotifyApiLimitKind.RateLimit));
		});
	}

	[Test]
	public async Task GetCatalogAsync_cancelled_after_the_first_page_requests_no_further_page()
	{
		using var cancellation = new CancellationTokenSource();
		var spotify = new FakeSpotifyCatalog { LikedSongs = 120, OnCatalogRequest = cancellation.Cancel };
		var player = CreatePlayer(spotify);

		Assert.That(async () => await player.GetCatalogAsync("entry",
				MusicPlayerCatalogItemKind.Track,
				null,
				cancellation.Token),
			Throws.InstanceOf<OperationCanceledException>());
		Assert.That(spotify.CatalogRequests.Count(), Is.EqualTo(1));
	}

	private static SpotifyMusicPlayer CreatePlayer(FakeSpotifyCatalog spotify)
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
		var player = new SpotifyMusicPlayer();
		player.Connect(SpotifyClientConfig.CreateDefault()
				.WithAuthenticator(new SpotifyAccessTokenAuthenticator(tokens))
				.WithHTTPClient(spotify.Http),
			tokens);
		return player;
	}

	// Answers like the Web API does for a Development Mode app since February 2026: a missing limit
	// means the endpoint default, and a search limit above 10 is refused.
	private sealed class FakeSpotifyCatalog
	{
		internal const int SearchDefaultLimit = 5;

		private const int SearchMaxLimit = 10;
		private const int LibraryDefaultLimit = 20;
		private const int LibraryMaxLimit = 50;

		public FakeSpotifyCatalog()
		{
			Http = new StubSpotifyHttpClient { Handler = Answer };
		}

		public int LikedSongs { get; init; }

		public int OwnPlaylists { get; init; }

		public int SearchHits { get; init; } = 1000;

		public bool LikedWhileReading { get; init; }

		public int? RateLimitedFromRequest { get; init; }

		public Action? OnCatalogRequest { get; init; }

		public StubSpotifyHttpClient Http { get; }

		public IEnumerable<IRequest> CatalogRequests
			=> Http.Requests.Where(r => !r.Endpoint.ToString().Contains("me/player", StringComparison.Ordinal));

		private Response Answer(IRequest request)
		{
			var path = request.Endpoint.ToString();
			var catalogRequests = CatalogRequests.Count();
			OnCatalogRequest?.Invoke();
			if (catalogRequests >= RateLimitedFromRequest)
			{
				return new Response(new Dictionary<string, string> { ["Retry-After"] = "30" })
				{
					StatusCode = HttpStatusCode.TooManyRequests, ContentType = "application/json", Body = "{}"
				};
			}

			if (path.EndsWith("me/tracks", StringComparison.Ordinal))
			{
				var shift = LikedWhileReading && catalogRequests > 1 ? 1 : 0;
				return Page(request, LibraryDefaultLimit, LibraryMaxLimit, LikedSongs + shift, n => new
				{
					added_at = "2026-01-01T00:00:00Z", track = Track(n - shift < 0 ? "just-liked" : $"liked-{n - shift}")
				});
			}

			if (path.EndsWith("me/playlists", StringComparison.Ordinal))
			{
				return Page(request, LibraryDefaultLimit, LibraryMaxLimit, OwnPlaylists, n => Playlist($"playlist-{n}"));
			}

			if (path.EndsWith("search", StringComparison.Ordinal))
			{
				var tracks = request.Parameters["type"] == "track";
				var page = Page(request,
					SearchDefaultLimit,
					SearchMaxLimit,
					SearchHits,
					n => tracks ? Track($"hit-{n}") : Playlist($"hit-{n}"));
				return page.StatusCode != HttpStatusCode.OK
					? page
					: Json(HttpStatusCode.OK, $"{{\"{(tracks ? "tracks" : "playlists")}\": {page.Body}}}");
			}

			return Json(HttpStatusCode.NotFound, "{}");
		}

		internal static int Offset(IRequest request)
			=> request.Parameters.TryGetValue("offset", out var offset)
				? int.Parse(offset, CultureInfo.InvariantCulture)
				: 0;

		private static Response Page(IRequest request, int defaultLimit, int maxLimit, int total, Func<int, object> item)
		{
			var limit = request.Parameters.TryGetValue("limit", out var l)
				? int.Parse(l, CultureInfo.InvariantCulture)
				: defaultLimit;
			var offset = Offset(request);
			if (limit > maxLimit)
			{
				return Json(HttpStatusCode.BadRequest, """{"error":{"status":400,"message":"Invalid limit"}}""");
			}

			var items = Enumerable.Range(offset, Math.Max(0, Math.Min(limit, total - offset))).Select(item).ToList();
			var next = offset + items.Count < total ? "https://api.spotify.com/v1/next" : null;
			return Json(HttpStatusCode.OK,
				JsonSerializer.Serialize(new { href = "https://api.spotify.com/v1/page", items, limit, offset, total, next }));
		}

		private static object Track(string id)
			=> new
			{
				type = "track",
				id,
				uri = $"spotify:track:{id}",
				name = id,
				duration_ms = 1000,
				artists = Array.Empty<object>(),
				album = new { name = "Album", images = Array.Empty<object>() }
			};

		private static object Playlist(string id)
			=> new { type = "playlist", id, uri = $"spotify:playlist:{id}", name = id, images = Array.Empty<object>() };

		private static Response Json(HttpStatusCode statusCode, string body)
			=> new(new Dictionary<string, string>())
			{
				StatusCode = statusCode,
				ContentType = "application/json",
				Body = body
			};
	}
}
