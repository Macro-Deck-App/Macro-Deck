using System.Net;
using MacroDeckHost.Integrations.Spotify;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.MusicPlayer.Actions;
using Serilog;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Http;

namespace MacroDeckHost.Tests.UnitTests.Spotify;

[TestFixture]
internal sealed class SpotifyLibraryActionTests
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

	private const string PlaylistsPageJson =
		"""
		{ "items": [ { "id": "pl-1", "name": "My Playlist", "owner": { "display_name": "Alice" }, "images": [] } ],
		  "limit": 20, "offset": 0, "total": 1, "href": "https://api.spotify.com/v1/me/playlists" }
		""";

	[Test]
	public async Task ToggleLiked_add_saves_the_current_track_to_the_unified_library_endpoint()
	{
		var http = new StubSpotifyHttpClient
		{
			Handler = request => IsLibraryWriteRequest(request)
				? Json(HttpStatusCode.OK, string.Empty)
				: Json(HttpStatusCode.OK, PlayingTrackJson)
		};
		var player = await PlayingPlayerAsync(http);

		await ExecuteAsync(ToggleLikedAction(player), mode: "add");

		var write = http.Requests.Single(IsLibraryWriteRequest);
		Assert.Multiple(() =>
		{
			Assert.That(write.Method, Is.EqualTo(HttpMethod.Put));
			Assert.That(write.Endpoint.ToString(), Is.EqualTo("me/library"));
			Assert.That(Uris(write), Is.EqualTo("spotify:track:track-1"));
		});
	}

	[Test]
	public async Task ToggleLiked_remove_removes_the_current_track_from_the_unified_library_endpoint()
	{
		var http = new StubSpotifyHttpClient
		{
			Handler = request => IsLibraryWriteRequest(request)
				? Json(HttpStatusCode.OK, string.Empty)
				: Json(HttpStatusCode.OK, PlayingTrackJson)
		};
		var player = await PlayingPlayerAsync(http);

		await ExecuteAsync(ToggleLikedAction(player), mode: "remove");

		var write = http.Requests.Single(IsLibraryWriteRequest);
		Assert.Multiple(() =>
		{
			Assert.That(write.Method, Is.EqualTo(HttpMethod.Delete));
			Assert.That(write.Endpoint.ToString(), Is.EqualTo("me/library"));
			Assert.That(Uris(write), Is.EqualTo("spotify:track:track-1"));
		});
	}

	[TestCase("[true]", "DELETE")]
	[TestCase("[false]", "PUT")]
	public async Task ToggleLiked_toggle_branches_on_the_check_items_answer(string checkBody, string expectedVerb)
	{
		var http = new StubSpotifyHttpClient
		{
			Handler = request => IsLibraryCheckRequest(request)
				? Json(HttpStatusCode.OK, checkBody)
				: IsLibraryWriteRequest(request)
					? Json(HttpStatusCode.OK, string.Empty)
					: Json(HttpStatusCode.OK, PlayingTrackJson)
		};
		var player = await PlayingPlayerAsync(http);

		await ExecuteAsync(ToggleLikedAction(player), mode: "toggle");

		var write = http.Requests.Single(IsLibraryWriteRequest);
		Assert.Multiple(() =>
		{
			Assert.That(write.Method, Is.EqualTo(new HttpMethod(expectedVerb)));
			Assert.That(Uris(write), Is.EqualTo("spotify:track:track-1"));
		});
	}

	[Test]
	public async Task ToggleLiked_is_a_noop_when_nothing_is_playing()
	{
		var http = new StubSpotifyHttpClient { Handler = _ => Json(HttpStatusCode.NoContent, string.Empty) };
		var player = CreatePlayer(http);
		await player.GetStateAsync();
		Assert.That(player.CurrentItem, Is.Null, "test setup: expected no current item");

		await ExecuteAsync(ToggleLikedAction(player), mode: "add");

		Assert.That(http.Requests.Any(IsLibraryWriteRequest), Is.False);
	}

	[Test]
	public async Task ToggleLiked_works_for_an_episode()
	{
		var http = new StubSpotifyHttpClient
		{
			Handler = request => IsLibraryWriteRequest(request)
				? Json(HttpStatusCode.OK, string.Empty)
				: Json(HttpStatusCode.OK, PlayingEpisodeJson)
		};
		var player = await PlayingPlayerAsync(http);

		await ExecuteAsync(ToggleLikedAction(player), mode: "add");

		var write = http.Requests.Single(IsLibraryWriteRequest);
		Assert.That(Uris(write), Is.EqualTo("spotify:episode:episode-1"));
	}

	[Test]
	public async Task PlaylistMembership_add_hits_the_playlist_items_endpoint()
	{
		var http = new StubSpotifyHttpClient
		{
			Handler = request => IsPlaylistItemsRequest(request)
				? Json(HttpStatusCode.OK, """{"snapshot_id":"snap-1"}""")
				: Json(HttpStatusCode.OK, PlayingTrackJson)
		};
		var player = await PlayingPlayerAsync(http);

		await ExecuteAsync(PlaylistMembershipAction(player), mode: "add", playlist: "pl-1");

		var write = http.Requests.Single(IsPlaylistItemsRequest);
		Assert.Multiple(() =>
		{
			Assert.That(write.Method, Is.EqualTo(HttpMethod.Post));
			Assert.That(write.Endpoint.ToString(), Is.EqualTo("playlists/pl-1/items"));
			Assert.That((string)write.Body!, Does.Contain("spotify:track:track-1"));
		});
	}

	[Test]
	public async Task PlaylistMembership_remove_hits_the_playlist_items_endpoint()
	{
		var http = new StubSpotifyHttpClient
		{
			Handler = request => IsPlaylistItemsRequest(request)
				? Json(HttpStatusCode.OK, """{"snapshot_id":"snap-1"}""")
				: Json(HttpStatusCode.OK, PlayingTrackJson)
		};
		var player = await PlayingPlayerAsync(http);

		await ExecuteAsync(PlaylistMembershipAction(player), mode: "remove", playlist: "pl-1");

		var write = http.Requests.Single(IsPlaylistItemsRequest);
		Assert.Multiple(() =>
		{
			Assert.That(write.Method, Is.EqualTo(HttpMethod.Delete));
			Assert.That(write.Endpoint.ToString(), Is.EqualTo("playlists/pl-1/items"));
			Assert.That((string)write.Body!, Does.Contain("spotify:track:track-1"));
		});
	}

	[Test]
	public async Task PlaylistMembership_is_a_noop_when_nothing_is_playing()
	{
		var http = new StubSpotifyHttpClient { Handler = _ => Json(HttpStatusCode.NoContent, string.Empty) };
		var player = CreatePlayer(http);
		await player.GetStateAsync();
		Assert.That(player.CurrentItem, Is.Null, "test setup: expected no current item");

		await ExecuteAsync(PlaylistMembershipAction(player), mode: "add", playlist: "pl-1");

		Assert.That(http.Requests.Any(IsPlaylistItemsRequest), Is.False);
	}

	[TestCase("pl-1")]
	[TestCase("spotify:playlist:pl-1")]
	[TestCase("https://open.spotify.com/playlist/pl-1?si=abcdef")]
	public async Task PlaylistMembership_accepts_a_bare_id_a_uri_or_a_web_url(string playlistValue)
	{
		var http = new StubSpotifyHttpClient
		{
			Handler = request => IsPlaylistItemsRequest(request)
				? Json(HttpStatusCode.OK, """{"snapshot_id":"snap-1"}""")
				: Json(HttpStatusCode.OK, PlayingTrackJson)
		};
		var player = await PlayingPlayerAsync(http);

		await ExecuteAsync(PlaylistMembershipAction(player), mode: "add", playlist: playlistValue);

		var write = http.Requests.Single(IsPlaylistItemsRequest);
		Assert.That(write.Endpoint.ToString(), Is.EqualTo("playlists/pl-1/items"));
	}

	[Test]
	public async Task PlaylistMembership_autocomplete_options_come_from_the_catalog()
	{
		var http = new StubSpotifyHttpClient
		{
			Handler = request => IsPlaylistsCatalogRequest(request)
				? Json(HttpStatusCode.OK, PlaylistsPageJson)
				: Json(HttpStatusCode.NoContent, string.Empty)
		};
		var player = CreatePlayer(http);
		var action = (IDynamicOptionsActionDefinition)PlaylistMembershipAction(player);

		var result = await action.GetDynamicOptionsAsync(new DynamicOptionsContext
			{
				ParameterName = "playlist",
				CurrentParameters = new Dictionary<string, object?> { [MusicPlayerActions.InstanceParameterName] = "" }
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.AllowsCustomValue, Is.True);
			Assert.That(result.Options, Has.Count.EqualTo(1));
			Assert.That(result.Options[0].Value, Is.EqualTo("pl-1"));
			Assert.That(TestLocalization.Resolve(result.Options[0].Label), Is.EqualTo("My Playlist"));
		});
	}

	[Test]
	public async Task PlaylistMembership_autocomplete_degrades_to_an_empty_custom_value_list_on_failure()
	{
		var http = new StubSpotifyHttpClient
		{
			Handler = request => IsPlaylistsCatalogRequest(request)
				? Json(HttpStatusCode.InternalServerError, string.Empty)
				: Json(HttpStatusCode.NoContent, string.Empty)
		};
		var player = CreatePlayer(http);
		var action = (IDynamicOptionsActionDefinition)PlaylistMembershipAction(player);

		var result = await action.GetDynamicOptionsAsync(new DynamicOptionsContext
			{
				ParameterName = "playlist",
				CurrentParameters = new Dictionary<string, object?> { [MusicPlayerActions.InstanceParameterName] = "" }
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.AllowsCustomValue, Is.True);
			Assert.That(result.Options, Is.Empty);
		});
	}

	[TestCase("")]
	[TestCase("   ")]
	[TestCase("spotify:playlist:")]
	[TestCase("https://open.spotify.com/album/al-1")]
	public async Task PlaylistMembership_is_a_noop_for_a_value_that_holds_no_playlist_id(string playlistValue)
	{
		var http = new StubSpotifyHttpClient
		{
			Handler = request => IsPlaylistItemsRequest(request)
				? Json(HttpStatusCode.OK, """{"snapshot_id":"snap-1"}""")
				: Json(HttpStatusCode.OK, PlayingTrackJson)
		};
		var player = await PlayingPlayerAsync(http);

		await ExecuteAsync(PlaylistMembershipAction(player), mode: "add", playlist: playlistValue);

		Assert.That(http.Requests.Any(IsPlaylistItemsRequest), Is.False);
	}

	[Test]
	public async Task PlaylistMembership_still_offers_the_instance_options()
	{
		var http = new StubSpotifyHttpClient { Handler = _ => Json(HttpStatusCode.NoContent, string.Empty) };
		var action = (IDynamicOptionsActionDefinition)PlaylistMembershipAction(CreatePlayer(http));

		var result = await action.GetDynamicOptionsAsync(new DynamicOptionsContext
			{
				ParameterName = MusicPlayerActions.InstanceParameterName,
				CurrentParameters = new Dictionary<string, object?>()
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Options.Select(o => o.Value), Does.Contain("entry-1"));
			Assert.That(http.Requests, Is.Empty);
		});
	}

	[Test]
	public async Task ToggleLiked_reports_whether_the_playing_track_is_saved()
	{
		var http = new StubSpotifyHttpClient
		{
			Handler = request => IsLibraryCheckRequest(request)
				? Json(HttpStatusCode.OK, "[true]")
				: Json(HttpStatusCode.OK, PlayingTrackJson)
		};
		var player = await PlayingPlayerAsync(http);

		var snapshot = await State(ToggleLikedAction(player));

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("liked"));
	}

	[Test]
	public async Task ToggleLiked_is_unavailable_when_nothing_is_playing()
	{
		var http = new StubSpotifyHttpClient { Handler = _ => Json(HttpStatusCode.NoContent, string.Empty) };
		var player = CreatePlayer(http);
		await player.GetStateAsync();

		var snapshot = await State(ToggleLikedAction(player));

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("unavailable"));
	}

	[Test]
	public async Task PlaylistMembership_reports_whether_the_playing_track_is_in_the_playlist()
	{
		var http = new StubSpotifyHttpClient
		{
			Handler = request => IsPlaylistItemsRequest(request)
				? Json(HttpStatusCode.OK, PlaylistWithPlayingTrackJson)
				: Json(HttpStatusCode.OK, PlayingTrackJson)
		};
		var player = await PlayingPlayerAsync(http);

		var snapshot = await State(PlaylistMembershipAction(player), playlist: "pl-1");

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("in-playlist"));
	}

	[Test]
	public async Task PlaylistMembership_reports_not_in_playlist_for_a_playlist_without_the_track()
	{
		var http = new StubSpotifyHttpClient
		{
			Handler = request => IsPlaylistItemsRequest(request)
				? Json(HttpStatusCode.OK, PlaylistWithOtherTrackJson)
				: Json(HttpStatusCode.OK, PlayingTrackJson)
		};
		var player = await PlayingPlayerAsync(http);

		var snapshot = await State(PlaylistMembershipAction(player), playlist: "pl-1");

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("not-in-playlist"));
	}

	[Test]
	public async Task PlaylistMembership_reads_the_playlist_once_for_repeated_polls()
	{
		var http = new StubSpotifyHttpClient
		{
			Handler = request => IsPlaylistItemsRequest(request)
				? Json(HttpStatusCode.OK, PlaylistWithPlayingTrackJson)
				: Json(HttpStatusCode.OK, PlayingTrackJson)
		};
		var player = await PlayingPlayerAsync(http);
		var action = PlaylistMembershipAction(player);

		await State(action, playlist: "pl-1");
		await State(action, playlist: "pl-1");
		await State(action, playlist: "pl-1");

		Assert.That(http.Requests.Count(IsPlaylistItemsRequest), Is.EqualTo(1));
	}

	[Test]
	public async Task PlaylistMembership_reports_no_state_before_a_playlist_is_chosen()
	{
		var http = new StubSpotifyHttpClient { Handler = _ => Json(HttpStatusCode.OK, PlayingTrackJson) };
		var player = await PlayingPlayerAsync(http);

		var snapshot = await State(PlaylistMembershipAction(player));

		Assert.Multiple(() =>
		{
			Assert.That(snapshot, Is.Null);
			Assert.That(http.Requests.Any(IsPlaylistItemsRequest), Is.False);
		});
	}

	private const string PlaylistWithPlayingTrackJson =
		"""
		{ "items": [ { "track": { "type": "track", "id": "track-1", "uri": "spotify:track:track-1",
		    "name": "Track One", "duration_ms": 200000, "artists": [], "album": { "name": "Album", "images": [] } } } ],
		  "limit": 100, "offset": 0, "total": 1 }
		""";

	private const string PlaylistWithOtherTrackJson =
		"""
		{ "items": [ { "track": { "type": "track", "id": "track-9", "uri": "spotify:track:track-9",
		    "name": "Track Nine", "duration_ms": 200000, "artists": [], "album": { "name": "Album", "images": [] } } } ],
		  "limit": 100, "offset": 0, "total": 1 }
		""";

	private static Task<ActionStateSnapshot?> State(IActionDefinition action, string? playlist = null)
	{
		var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			[MusicPlayerActions.InstanceParameterName] = ""
		};
		if (playlist is not null)
		{
			parameters["playlist"] = playlist;
		}

		return ((IStateProviderActionDefinition)action).GetActionStateAsync(parameters, CancellationToken.None);
	}

	private static IActionDefinition ToggleLikedAction(SpotifyMusicPlayer player)
		=> SpotifyLibraryActions.All(_ => player, () => [new MusicPlayerInstance("entry-1", "Spotify")])
			.Single(a => a.Id == "toggle-liked");

	private static IActionDefinition PlaylistMembershipAction(SpotifyMusicPlayer player)
		=> SpotifyLibraryActions.All(_ => player, () => [new MusicPlayerInstance("entry-1", "Spotify")])
			.Single(a => a.Id == "playlist-membership");

	private static Task<ActionResult> ExecuteAsync(IActionDefinition action, string mode, string? playlist = null)
	{
		var parameters = new Dictionary<string, object>
		{
			[MusicPlayerActions.InstanceParameterName] = "", ["mode"] = mode
		};
		if (playlist is not null)
		{
			parameters["playlist"] = playlist;
		}

		return action.CreateExecutor().ExecuteAsync(new ActionExecutionContext { Parameters = parameters });
	}

	private static string? Uris(IRequest request)
	{
		Assert.That(request.Body, Is.Null, "the /me/library write must not carry a request body");
		return request.Parameters.TryGetValue("uris", out var uris) ? uris : null;
	}

	private static bool IsLibraryWriteRequest(IRequest request)
		=> request.Endpoint.ToString() == "me/library";

	private static bool IsLibraryCheckRequest(IRequest request)
		=> request.Endpoint.ToString().Contains("library/contains", StringComparison.Ordinal);

	private static bool IsPlaylistItemsRequest(IRequest request)
		=> request.Endpoint.ToString().Contains("playlists/", StringComparison.Ordinal) &&
			request.Endpoint.ToString().Contains("/items", StringComparison.Ordinal);

	private static bool IsPlaylistsCatalogRequest(IRequest request)
		=> request.Endpoint.ToString().Contains("me/playlists", StringComparison.Ordinal);

	private static Response Json(HttpStatusCode statusCode, string body)
		=> new(new Dictionary<string, string>())
		{
			StatusCode = statusCode,
			ContentType = "application/json",
			Body = body
		};

	private static SpotifyMusicPlayer CreatePlayer(StubSpotifyHttpClient http)
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
				.WithHTTPClient(http),
			tokens);
		return player;
	}

	private static async Task<SpotifyMusicPlayer> PlayingPlayerAsync(StubSpotifyHttpClient http)
	{
		var player = CreatePlayer(http);
		await player.GetStateAsync();
		Assert.That(player.CurrentItem, Is.Not.Null, "test setup: expected a current item after the poll");
		return player;
	}
}
