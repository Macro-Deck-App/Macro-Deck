using System.Net;
using MacroDeckHost.Integrations.Spotify;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeck.Sdk.MusicPlayer;
using Serilog;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Http;

namespace MacroDeckHost.Tests.UnitTests.Spotify;

[TestFixture]
internal sealed class SpotifyMusicPlayerStateTests
{
	private static readonly string[] _pausedArtists = ["First Artist", "Second Artist"];

	private const string PausedPlaybackJson =
		"""
		{
		  "device": {
		    "id": "dev-1",
		    "is_active": true,
		    "name": "Desk",
		    "type": "Computer",
		    "volume_percent": 55
		  },
		  "repeat_state": "off",
		  "shuffle_state": false,
		  "timestamp": 1700000000000,
		  "progress_ms": 42000,
		  "is_playing": false,
		  "currently_playing_type": "track",
		  "item": {
		    "type": "track",
		    "id": "track-1",
		    "name": "Paused Track",
		    "duration_ms": 180000,
		    "artists": [{ "name": "First Artist" }, { "name": "Second Artist" }],
		    "album": {
		      "name": "Some Album",
		      "images": [{ "url": "https://example.test/cover.jpg", "width": 640, "height": 640 }]
		    }
		  }
		}
		""";

	private const string PlayingTrackJson =
		"""
		{
		  "device": { "id": "dev-1", "is_active": true, "name": "Desk", "type": "Computer", "volume_percent": 55 },
		  "repeat_state": "off",
		  "shuffle_state": false,
		  "progress_ms": 1000,
		  "is_playing": true,
		  "currently_playing_type": "track",
		  "item": {
		    "type": "track",
		    "id": "track-1",
		    "uri": "spotify:track:track-1",
		    "name": "Some Track",
		    "duration_ms": 200000,
		    "artists": [{ "name": "Artist" }],
		    "album": { "name": "Album", "images": [] },
		    "external_urls": { "spotify": "https://open.spotify.com/track/track-1" }
		  }
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
		  "item": {
		    "type": "episode",
		    "id": "episode-1",
		    "uri": "spotify:episode:episode-1",
		    "name": "Some Episode",
		    "duration_ms": 300000,
		    "show": { "name": "Show" },
		    "images": [],
		    "external_urls": { "spotify": "https://open.spotify.com/episode/episode-1" }
		  }
		}
		""";

	private const string PlayingTrackWithoutExternalUrlJson =
		"""
		{
		  "device": { "id": "dev-1", "is_active": true, "name": "Desk", "type": "Computer", "volume_percent": 55 },
		  "repeat_state": "off",
		  "shuffle_state": false,
		  "progress_ms": 1000,
		  "is_playing": true,
		  "currently_playing_type": "track",
		  "item": {
		    "type": "track",
		    "id": "track-2",
		    "uri": "spotify:track:track-2",
		    "name": "No External Url Track",
		    "duration_ms": 100000,
		    "artists": [],
		    "album": { "name": "Album", "images": [] }
		  }
		}
		""";

	private const string PlayingLocalTrackJson =
		"""
		{
		  "device": { "id": "dev-1", "is_active": true, "name": "Desk", "type": "Computer", "volume_percent": 55 },
		  "repeat_state": "off",
		  "shuffle_state": false,
		  "progress_ms": 1000,
		  "is_playing": true,
		  "currently_playing_type": "track",
		  "item": {
		    "type": "track",
		    "id": null,
		    "uri": "spotify:local:Artist:Album:Track:123",
		    "name": "Local Track",
		    "duration_ms": 0,
		    "artists": [],
		    "album": { "name": "", "images": [] },
		    "is_local": true
		  }
		}
		""";

	private const string PlaybackWithoutItemJson =
		"""
		{
		  "device": { "id": "dev-1", "is_active": true, "name": "Desk", "type": "Computer", "volume_percent": 55 },
		  "repeat_state": "off",
		  "shuffle_state": false,
		  "progress_ms": 0,
		  "is_playing": false,
		  "currently_playing_type": "unknown",
		  "item": null
		}
		""";

	[Test]
	public void IsTransient_TooManyRequestsException_IsTransient()
	{
		Assert.That(SpotifyMusicPlayer.IsTransient(new APITooManyRequestsException()), Is.True);
	}

	[TestCase(HttpStatusCode.TooManyRequests)]
	[TestCase(HttpStatusCode.InternalServerError)]
	[TestCase(HttpStatusCode.BadGateway)]
	[TestCase(HttpStatusCode.ServiceUnavailable)]
	[TestCase(HttpStatusCode.GatewayTimeout)]
	public void IsTransient_RateLimitAndServerErrors_AreTransient(HttpStatusCode statusCode)
	{
		Assert.That(SpotifyMusicPlayer.IsTransient(ApiException(statusCode)), Is.True);
	}

	[TestCase(HttpStatusCode.Forbidden)]
	[TestCase(HttpStatusCode.NotFound)]
	[TestCase(HttpStatusCode.BadRequest)]
	public void IsTransient_ClientErrors_AreNotTransient(HttpStatusCode statusCode)
	{
		Assert.That(SpotifyMusicPlayer.IsTransient(ApiException(statusCode)), Is.False);
	}

	[Test]
	public void IsTransient_NetworkLevelBlips_AreTransient()
	{
		Assert.Multiple(() =>
		{
			Assert.That(SpotifyMusicPlayer.IsTransient(new HttpRequestException("boom")), Is.True);
			Assert.That(SpotifyMusicPlayer.IsTransient(new TaskCanceledException("timeout")), Is.True);
			Assert.That(SpotifyMusicPlayer.IsTransient(new IOException("reset")), Is.True);
		});
	}

	[Test]
	public void IsTransient_UnrelatedException_IsNotTransient()
	{
		Assert.That(SpotifyMusicPlayer.IsTransient(new InvalidOperationException()), Is.False);
	}

	[Test]
	public void IsInvalidGrant_BadRequestWithInvalidGrantBody_IsTrue()
	{
		var ex = ApiException(HttpStatusCode.BadRequest,
			"""{"error":"invalid_grant","error_description":"Refresh token revoked"}""");

		Assert.That(SpotifyMusicPlayer.IsInvalidGrant(ex), Is.True);
	}

	[Test]
	public void IsInvalidGrant_BadRequestWithoutInvalidGrant_IsFalse()
	{
		var ex = ApiException(HttpStatusCode.BadRequest, """{"error":"invalid_request"}""");

		Assert.That(SpotifyMusicPlayer.IsInvalidGrant(ex), Is.False);
	}

	[Test]
	public void IsInvalidGrant_WrappedInInnerException_IsTrue()
	{
		var inner = ApiException(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""");
		var wrapped = new InvalidOperationException("refresh failed", inner);

		Assert.That(SpotifyMusicPlayer.IsInvalidGrant(wrapped), Is.True);
	}

	[Test]
	public void IsInvalidGrant_UnrelatedException_IsFalse()
	{
		Assert.Multiple(() =>
		{
			Assert.That(SpotifyMusicPlayer.IsInvalidGrant(new HttpRequestException("boom")), Is.False);
			Assert.That(SpotifyMusicPlayer.IsInvalidGrant(ApiException(HttpStatusCode.InternalServerError)), Is.False);
		});
	}

	[Test]
	public async Task GetStateAsync_reports_disconnected_when_playback_read_is_unauthorized()
	{
		var http = new StubSpotifyHttpClient
		{
			Handler = _ => Json(HttpStatusCode.Unauthorized,
				"""{"error":{"status":401,"message":"Access token missing"}}""")
		};
		var player = CreatePlayer(http);

		var state = await player.GetStateAsync();

		Assert.Multiple(() =>
		{
			Assert.That(state.IsConnected, Is.False);
			Assert.That(player.AuthenticationState, Is.EqualTo(SpotifyAuthenticationState.ReauthorizationRequired));
		});
	}

	[Test]
	public async Task GetStateAsync_reports_disconnected_when_devices_read_is_unauthorized()
	{
		// Nothing is playing (204 on playback), so the idle-state path reads the device list; a token
		// rejection there must not be swallowed into a connected idle state.
		var http = new StubSpotifyHttpClient
		{
			Handler = request => IsDevicesRequest(request)
				? Json(HttpStatusCode.Unauthorized, """{"error":{"status":401,"message":"Access token missing"}}""")
				: Json(HttpStatusCode.NoContent, string.Empty)
		};
		var player = CreatePlayer(http);

		var state = await player.GetStateAsync();

		Assert.Multiple(() =>
		{
			Assert.That(state.IsConnected, Is.False);
			Assert.That(player.AuthenticationState, Is.EqualTo(SpotifyAuthenticationState.ReauthorizationRequired));
		});
	}

	[Test]
	public async Task GetStateAsync_reports_disconnected_when_the_refresh_token_is_rejected()
	{
		var oauth = new FakeSpotifyOAuthClient();
		oauth.Results.Enqueue(FakeSpotifyOAuthClient.Rejected());
		var http = new StubSpotifyHttpClient { Handler = _ => Json(HttpStatusCode.NoContent, string.Empty) };
		var player = CreatePlayerWithExpiredToken(oauth, http);

		var state = await player.GetStateAsync();

		Assert.Multiple(() =>
		{
			Assert.That(state.IsConnected, Is.False);
			Assert.That(player.AuthenticationState, Is.EqualTo(SpotifyAuthenticationState.ReauthorizationRequired));
		});
	}

	[Test]
	public async Task GetStateAsync_keeps_the_last_state_when_the_token_endpoint_is_unreachable()
	{
		var oauth = new FakeSpotifyOAuthClient();
		oauth.Results.Enqueue(() => new SpotifyRefreshedToken("fresh-access", "fresh-refresh", 0));
		oauth.Results.Enqueue(FakeSpotifyOAuthClient.Unreachable());
		var http = new StubSpotifyHttpClient { Handler = _ => Json(HttpStatusCode.NoContent, string.Empty) };
		var player = CreatePlayerWithExpiredToken(oauth, http);

		var connected = await player.GetStateAsync();
		var afterOutage = await player.GetStateAsync();

		Assert.Multiple(() =>
		{
			Assert.That(connected.IsConnected, Is.True);
			// Not Disconnected: a network blink must not tear down the now-playing UI or raise an issue.
			Assert.That(afterOutage.IsConnected, Is.True);
			Assert.That(player.AuthenticationState,
				Is.EqualTo(SpotifyAuthenticationState.TemporarilyUnavailable));
		});
	}

	[Test]
	public async Task GetStateAsync_stops_within_its_cancellation_boundary_when_the_token_endpoint_stalls()
	{
		var oauth = new FakeSpotifyOAuthClient { Stall = true };
		var http = new StubSpotifyHttpClient { Handler = _ => Json(HttpStatusCode.NoContent, string.Empty) };
		var player = CreatePlayerWithExpiredToken(oauth, http);

		using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

		Assert.That(async () => await player.GetStateAsync(caller.Token),
			Throws.InstanceOf<OperationCanceledException>());
		await Task.CompletedTask;
	}

	[Test]
	public async Task GetStateAsync_sends_the_current_access_token_and_never_refreshes_inside_the_request()
	{
		var oauth = new FakeSpotifyOAuthClient();
		var seen = new List<string?>();
		var http = new StubSpotifyHttpClient
		{
			Handler = request =>
			{
				seen.Add(request.Headers.TryGetValue("Authorization", out var header) ? header : null);
				return Json(HttpStatusCode.NoContent, string.Empty);
			}
		};
		var tokens = Tokens(oauth, TimeSpan.FromHours(1));
		var player = new SpotifyMusicPlayer();
		player.Connect(Config(http, tokens), tokens);

		await player.GetStateAsync();

		Assert.Multiple(() =>
		{
			Assert.That(seen, Has.All.EqualTo("Bearer token"));
			Assert.That(oauth.RefreshCount, Is.EqualTo(0));
		});
	}

	[Test]
	public async Task GetStateAsync_clears_auth_failure_once_calls_succeed()
	{
		var unauthorized = true;
		var http = new StubSpotifyHttpClient
		{
			Handler = request =>
			{
				if (unauthorized)
				{
					return Json(HttpStatusCode.Unauthorized,
						"""{"error":{"status":401,"message":"Access token missing"}}""");
				}

				return IsDevicesRequest(request)
					? Json(HttpStatusCode.OK, """{"devices":[]}""")
					: Json(HttpStatusCode.NoContent, string.Empty);
			}
		};
		var player = CreatePlayer(http);

		await player.GetStateAsync();
		Assert.That(player.AuthenticationState, Is.EqualTo(SpotifyAuthenticationState.ReauthorizationRequired));

		unauthorized = false;
		var state = await player.GetStateAsync();

		Assert.Multiple(() =>
		{
			Assert.That(state.IsConnected, Is.True);
			Assert.That(player.AuthenticationState, Is.EqualTo(SpotifyAuthenticationState.Valid));
		});
	}

	[Test]
	public async Task GetStateAsync_keeps_track_metadata_when_playback_is_paused()
	{
		// A paused playback context is non-null but reports is_playing:false. It still carries the full
		// track, so pausing must not drop the now-playing state back to an empty idle snapshot.
		var http = new StubSpotifyHttpClient
		{
			Handler = _ => Json(HttpStatusCode.OK, PausedPlaybackJson)
		};
		var player = CreatePlayer(http);

		var state = await player.GetStateAsync();

		Assert.Multiple(() =>
		{
			Assert.That(state.IsConnected, Is.True);
			Assert.That(state.PlaybackState, Is.EqualTo(PlaybackState.Paused));
			Assert.That(state.TrackName, Is.EqualTo("Paused Track"));
			Assert.That(state.Artists, Is.EqualTo(_pausedArtists));
			Assert.That(state.AlbumName, Is.EqualTo("Some Album"));
			Assert.That(state.ArtworkId, Is.Not.Null.And.Not.Empty);
			Assert.That(state.Duration, Is.EqualTo(TimeSpan.FromMilliseconds(180000)));
			Assert.That(state.Position, Is.EqualTo(TimeSpan.FromMilliseconds(42000)));
			Assert.That(state.VolumePercent, Is.EqualTo(55));
			Assert.That(state.DeviceName, Is.EqualTo("Desk"));
		});
	}

	[Test]
	public async Task GetStateAsync_resolves_artwork_for_paused_playback()
	{
		var http = new StubSpotifyHttpClient
		{
			Handler = _ => Json(HttpStatusCode.OK, PausedPlaybackJson)
		};
		var player = CreatePlayer(http);

		var state = await player.GetStateAsync();

		Assert.That(state.ArtworkId, Is.Not.Null);
	}

	[Test]
	public async Task GetStateAsync_maps_playback_without_an_item_without_throwing()
	{
		var http = new StubSpotifyHttpClient
		{
			Handler = _ => Json(HttpStatusCode.OK,
				"""
				{
				  "device": { "id": "dev-1", "is_active": true, "name": "Desk", "type": "Computer", "volume_percent": 55 },
				  "repeat_state": "off",
				  "shuffle_state": false,
				  "progress_ms": 0,
				  "is_playing": false,
				  "currently_playing_type": "unknown",
				  "item": null
				}
				""")
		};
		var player = CreatePlayer(http);

		var state = await player.GetStateAsync();

		Assert.Multiple(() =>
		{
			Assert.That(state.IsConnected, Is.True);
			Assert.That(state.TrackName, Is.Null);
			Assert.That(state.Artists, Is.Empty);
			Assert.That(state.ArtworkId, Is.Null);
			Assert.That(state.Duration, Is.Null);
			Assert.That(state.DeviceName, Is.EqualTo("Desk"));
		});
	}

	[Test]
	public async Task GetStateAsync_reports_connected_and_stopped_when_nothing_is_loaded()
	{
		var http = new StubSpotifyHttpClient
		{
			Handler = request => IsDevicesRequest(request)
				? Json(HttpStatusCode.OK,
					"""{"devices":[{"id":"dev-1","is_active":true,"name":"Desk","type":"Computer","volume_percent":30}]}""")
				: Json(HttpStatusCode.NoContent, string.Empty)
		};
		var player = CreatePlayer(http);

		var state = await player.GetStateAsync();

		Assert.Multiple(() =>
		{
			Assert.That(state.IsConnected, Is.True);
			Assert.That(state.PlaybackState, Is.EqualTo(PlaybackState.Stopped));
			Assert.That(state.TrackName, Is.Null);
			Assert.That(state.ArtworkId, Is.Null);
			Assert.That(state.Duration, Is.Null);
			Assert.That(state.VolumePercent, Is.EqualTo(30));
			Assert.That(state.DeviceName, Is.EqualTo("Desk"));
		});
	}

	[Test]
	public async Task Connect_resets_auth_failure()
	{
		var http = new StubSpotifyHttpClient
		{
			Handler = _ => Json(HttpStatusCode.Unauthorized,
				"""{"error":{"status":401,"message":"Access token missing"}}""")
		};
		var player = CreatePlayer(http);
		await player.GetStateAsync();
		Assert.That(player.AuthenticationState, Is.EqualTo(SpotifyAuthenticationState.ReauthorizationRequired));

		var reconnected = Tokens();
		player.Connect(Config(http, reconnected), reconnected);

		Assert.That(player.AuthenticationState, Is.EqualTo(SpotifyAuthenticationState.Valid));
	}

	[Test]
	public async Task GetStateAsync_captures_the_current_track_with_its_url()
	{
		var http = new StubSpotifyHttpClient { Handler = _ => Json(HttpStatusCode.OK, PlayingTrackJson) };
		var player = CreatePlayer(http);

		await player.GetStateAsync();

		Assert.Multiple(() =>
		{
			Assert.That(player.CurrentItem, Is.Not.Null);
			Assert.That(player.CurrentItem!.Id, Is.EqualTo("track-1"));
			Assert.That(player.CurrentItem!.Uri, Is.EqualTo("spotify:track:track-1"));
			Assert.That(player.CurrentItem!.Url, Is.EqualTo("https://open.spotify.com/track/track-1"));
		});
	}

	[Test]
	public async Task GetStateAsync_captures_the_current_episode_with_its_url()
	{
		var http = new StubSpotifyHttpClient { Handler = _ => Json(HttpStatusCode.OK, PlayingEpisodeJson) };
		var player = CreatePlayer(http);

		await player.GetStateAsync();

		Assert.Multiple(() =>
		{
			Assert.That(player.CurrentItem, Is.Not.Null);
			Assert.That(player.CurrentItem!.Id, Is.EqualTo("episode-1"));
			Assert.That(player.CurrentItem!.Uri, Is.EqualTo("spotify:episode:episode-1"));
			Assert.That(player.CurrentItem!.Url, Is.EqualTo("https://open.spotify.com/episode/episode-1"));
		});
	}

	[Test]
	public async Task GetStateAsync_derives_the_url_when_external_urls_is_absent()
	{
		var http = new StubSpotifyHttpClient
		{
			Handler = _ => Json(HttpStatusCode.OK, PlayingTrackWithoutExternalUrlJson)
		};
		var player = CreatePlayer(http);

		await player.GetStateAsync();

		Assert.That(player.CurrentItem?.Url, Is.EqualTo("https://open.spotify.com/track/track-2"));
	}

	[Test]
	public async Task GetStateAsync_ignores_a_local_file()
	{
		var http = new StubSpotifyHttpClient { Handler = _ => Json(HttpStatusCode.OK, PlayingLocalTrackJson) };
		var player = CreatePlayer(http);

		await player.GetStateAsync();

		Assert.That(player.CurrentItem, Is.Null);
	}

	[Test]
	public async Task GetStateAsync_clears_the_current_item_when_nothing_is_loaded()
	{
		var playing = true;
		var http = new StubSpotifyHttpClient
		{
			Handler = _ => playing
				? Json(HttpStatusCode.OK, PlayingTrackJson)
				: Json(HttpStatusCode.NoContent, string.Empty)
		};
		var time = new ManualTimeProvider();
		var player = CreatePlayer(http, time);
		await player.GetStateAsync();
		Assert.That(player.CurrentItem, Is.Not.Null);

		playing = false;
		time.Advance(SpotifyPollSchedule.PausedInterval);
		await player.GetStateAsync();

		Assert.That(player.CurrentItem, Is.Null);
	}

	[Test]
	public async Task GetStateAsync_clears_the_current_item_when_the_item_is_null()
	{
		var itemPresent = true;
		var http = new StubSpotifyHttpClient
		{
			Handler = _ => Json(HttpStatusCode.OK, itemPresent ? PlayingTrackJson : PlaybackWithoutItemJson)
		};
		var time = new ManualTimeProvider();
		var player = CreatePlayer(http, time);
		await player.GetStateAsync();
		Assert.That(player.CurrentItem, Is.Not.Null);

		itemPresent = false;
		time.Advance(SpotifyPollSchedule.PausedInterval);
		await player.GetStateAsync();

		Assert.That(player.CurrentItem, Is.Null);
	}

	[Test]
	public async Task GetStateAsync_clears_the_current_item_when_unauthorized()
	{
		var unauthorized = false;
		var http = new StubSpotifyHttpClient
		{
			Handler = _ => unauthorized
				? Json(HttpStatusCode.Unauthorized, """{"error":{"status":401,"message":"Access token missing"}}""")
				: Json(HttpStatusCode.OK, PlayingTrackJson)
		};
		var time = new ManualTimeProvider();
		var player = CreatePlayer(http, time);
		await player.GetStateAsync();
		Assert.That(player.CurrentItem, Is.Not.Null);

		unauthorized = true;
		time.Advance(SpotifyPollSchedule.PausedInterval);
		await player.GetStateAsync();

		Assert.That(player.CurrentItem, Is.Null);
	}

	[Test]
	public async Task Disconnect_clears_the_current_item()
	{
		var http = new StubSpotifyHttpClient { Handler = _ => Json(HttpStatusCode.OK, PlayingTrackJson) };
		var player = CreatePlayer(http);
		await player.GetStateAsync();
		Assert.That(player.CurrentItem, Is.Not.Null);

		player.Disconnect();

		Assert.That(player.CurrentItem, Is.Null);
	}

	[Test]
	public async Task GetStateAsync_keeps_the_current_item_when_the_token_endpoint_is_unreachable()
	{
		// Pins the Publish vs "return LastState" split in GetStateAsync: a network blink on the token
		// endpoint must not blank the URL a widget or the current-track variable is already showing.
		var oauth = new FakeSpotifyOAuthClient();
		oauth.Results.Enqueue(() => new SpotifyRefreshedToken("fresh-access", "fresh-refresh", 0));
		oauth.Results.Enqueue(FakeSpotifyOAuthClient.Unreachable());
		var http = new StubSpotifyHttpClient { Handler = _ => Json(HttpStatusCode.OK, PlayingTrackJson) };
		var player = CreatePlayerWithExpiredToken(oauth, http);

		await player.GetStateAsync();
		Assert.That(player.CurrentItem, Is.Not.Null);

		await player.GetStateAsync();

		Assert.That(player.CurrentItem, Is.Not.Null);
	}

	[Test]
	public async Task GetStateAsync_recovers_from_a_401_by_forcing_one_refresh()
	{
		var oauth = new FakeSpotifyOAuthClient();
		var http = new StubSpotifyHttpClient
		{
			Handler = request =>
			{
				if (Authorization(request) == "Bearer token")
				{
					return Json(HttpStatusCode.Unauthorized,
						"""{"error":{"status":401,"message":"Access token expired"}}""");
				}

				return IsDevicesRequest(request)
					? Json(HttpStatusCode.OK, """{"devices":[]}""")
					: Json(HttpStatusCode.NoContent, string.Empty);
			}
		};
		var tokens = Tokens(oauth, TimeSpan.FromHours(1));
		var player = new SpotifyMusicPlayer();
		player.Connect(Config(http, tokens), tokens);

		var state = await player.GetStateAsync();

		Assert.Multiple(() =>
		{
			Assert.That(state.IsConnected, Is.True);
			Assert.That(oauth.RefreshCount, Is.EqualTo(1));
			Assert.That(player.AuthenticationState, Is.EqualTo(SpotifyAuthenticationState.Valid));
		});
	}

	[Test]
	public async Task GetStateAsync_latches_reauthorization_when_the_forced_refresh_does_not_help()
	{
		var oauth = new FakeSpotifyOAuthClient();
		var http = new StubSpotifyHttpClient
		{
			Handler = _ => Json(HttpStatusCode.Unauthorized,
				"""{"error":{"status":401,"message":"Access token expired"}}""")
		};
		var tokens = Tokens(oauth, TimeSpan.FromHours(1));
		var player = new SpotifyMusicPlayer();
		player.Connect(Config(http, tokens), tokens);

		var state = await player.GetStateAsync();
		await player.GetStateAsync();
		await player.GetStateAsync();

		Assert.Multiple(() =>
		{
			Assert.That(state.IsConnected, Is.False);
			Assert.That(player.AuthenticationState, Is.EqualTo(SpotifyAuthenticationState.ReauthorizationRequired));
			// Exactly one forced refresh for the whole 401 storm - a dead grant must not spend a refresh
			// token per poll tick.
			Assert.That(oauth.RefreshCount, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task GetStateAsync_keeps_the_last_state_when_the_401_recovery_refresh_cannot_reach_spotify()
	{
		var oauth = new FakeSpotifyOAuthClient();
		oauth.Results.Enqueue(FakeSpotifyOAuthClient.Unreachable());
		var unauthorized = false;
		var http = new StubSpotifyHttpClient
		{
			Handler = _ => unauthorized
				? Json(HttpStatusCode.Unauthorized, """{"error":{"status":401,"message":"Access token expired"}}""")
				: Json(HttpStatusCode.OK, PlayingTrackJson)
		};
		var tokens = Tokens(oauth, TimeSpan.FromHours(1));
		var player = new SpotifyMusicPlayer();
		player.Connect(Config(http, tokens), tokens);

		await player.GetStateAsync();
		unauthorized = true;
		var state = await player.GetStateAsync();

		Assert.Multiple(() =>
		{
			Assert.That(state.IsConnected, Is.True);
			Assert.That(player.AuthenticationState,
				Is.Not.EqualTo(SpotifyAuthenticationState.ReauthorizationRequired));
		});
	}

	[Test]
	public async Task GetStateAsync_degrades_to_disconnected_once_the_last_state_goes_stale()
	{
		var failing = false;
		var http = new StubSpotifyHttpClient
		{
			Handler = _ => failing
				? throw new HttpRequestException("connection reset")
				: Json(HttpStatusCode.OK, PlayingTrackJson)
		};
		var time = new ManualTimeProvider();
		var tokens = Tokens();
		var player = new SpotifyMusicPlayer(lastStateLifetime: TimeSpan.Zero, timeProvider: time);
		player.Connect(Config(http, tokens), tokens);
		await player.GetStateAsync();

		failing = true;
		time.Advance(SpotifyPollSchedule.PausedInterval);
		var state = await player.GetStateAsync();

		Assert.Multiple(() =>
		{
			Assert.That(state.IsConnected, Is.False);
			Assert.That(player.CurrentItem, Is.Null);
		});
	}

	[Test]
	public async Task Connect_resets_the_previous_generations_last_state()
	{
		var failing = false;
		var http = new StubSpotifyHttpClient
		{
			Handler = _ => failing
				? throw new HttpRequestException("connection reset")
				: Json(HttpStatusCode.OK, PlayingTrackJson)
		};
		var tokens = Tokens();
		var player = new SpotifyMusicPlayer();
		player.Connect(Config(http, tokens), tokens);
		await player.GetStateAsync();
		Assert.That(player.LastState.IsConnected, Is.True);

		var reconnected = Tokens();
		player.Connect(Config(http, reconnected), reconnected);
		failing = true;
		var state = await player.GetStateAsync();

		Assert.That(state.IsConnected, Is.False);
	}

	[Test]
	public async Task GetStateAsync_maps_playback_without_a_device_without_throwing()
	{
		var http = new StubSpotifyHttpClient
		{
			Handler = _ => Json(HttpStatusCode.OK,
				"""
				{
				  "repeat_state": "off",
				  "shuffle_state": false,
				  "progress_ms": 1000,
				  "is_playing": true,
				  "currently_playing_type": "track",
				  "item": {
				    "type": "track",
				    "id": "track-1",
				    "uri": "spotify:track:track-1",
				    "name": "Some Track",
				    "duration_ms": 200000,
				    "artists": [{ "name": "Artist" }],
				    "album": { "name": "Album", "images": [] }
				  }
				}
				""")
		};
		var player = CreatePlayer(http);

		var state = await player.GetStateAsync();

		Assert.Multiple(() =>
		{
			Assert.That(state.IsConnected, Is.True);
			Assert.That(state.TrackName, Is.EqualTo("Some Track"));
			Assert.That(state.VolumePercent, Is.Null);
			Assert.That(state.DeviceName, Is.Null);
		});
	}

	[Test]
	public async Task GetStateAsync_pauses_polling_for_the_retry_after_window_on_a_429()
	{
		var http = new StubSpotifyHttpClient
		{
			Handler = _ => new Response(new Dictionary<string, string> { ["Retry-After"] = "30" })
			{
				StatusCode = HttpStatusCode.TooManyRequests,
				ContentType = "application/json",
				Body = """{"error":{"status":429,"message":"rate limited"}}"""
			}
		};
		var player = CreatePlayer(http);

		await player.GetStateAsync();
		await player.GetStateAsync();
		await player.GetStateAsync();

		Assert.That(http.Requests, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task GetStateAsync_pauses_polling_on_a_429_without_a_retry_after_header()
	{
		var http = new StubSpotifyHttpClient
		{
			Handler = _ => new Response(new Dictionary<string, string>())
			{
				StatusCode = HttpStatusCode.TooManyRequests,
				ContentType = "application/json",
				Body = """{"error":{"status":429,"message":"rate limited"}}"""
			}
		};
		var player = CreatePlayer(http);

		await player.GetStateAsync();
		await player.GetStateAsync();

		Assert.That(http.Requests, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task GetStateAsync_reuses_the_idle_device_snapshot_between_polls()
	{
		var http = new StubSpotifyHttpClient
		{
			Handler = request => IsDevicesRequest(request)
				? Json(HttpStatusCode.OK,
					"""{"devices":[{"id":"dev-1","is_active":true,"name":"Desk","type":"Computer","volume_percent":30}]}""")
				: Json(HttpStatusCode.NoContent, string.Empty)
		};
		var player = CreatePlayer(http);

		var first = await player.GetStateAsync();
		var second = await player.GetStateAsync();

		Assert.Multiple(() =>
		{
			// The devices read only decorates the idle state; it must not ride every 1.5s tick.
			Assert.That(http.Requests.Count(IsDevicesRequest), Is.EqualTo(1));
			Assert.That(second.IsConnected, Is.True);
			Assert.That(second.DeviceName, Is.EqualTo(first.DeviceName));
		});
	}

	private static string? Authorization(IRequest request)
		=> request.Headers.TryGetValue("Authorization", out var header) ? header : null;

	private static bool IsDevicesRequest(IRequest request)
		=> request.Endpoint.ToString().Contains("devices", StringComparison.Ordinal);

	private static SpotifyMusicPlayer CreatePlayer(StubSpotifyHttpClient http, TimeProvider? timeProvider = null)
	{
		var tokens = Tokens();
		var player = new SpotifyMusicPlayer(timeProvider: timeProvider);
		player.Connect(Config(http, tokens), tokens);
		return player;
	}

	private static SpotifyMusicPlayer CreatePlayerWithExpiredToken(
		FakeSpotifyOAuthClient oauth,
		StubSpotifyHttpClient http)
	{
		var tokens = Tokens(oauth, TimeSpan.Zero);
		var player = new SpotifyMusicPlayer();
		player.Connect(Config(http, tokens), tokens);
		return player;
	}

	private static SpotifyClientConfig Config(IHTTPClient http, ISpotifyAccessTokenSource tokens)
		=> SpotifyClientConfig.CreateDefault()
			.WithAuthenticator(new SpotifyAccessTokenAuthenticator(tokens))
			.WithHTTPClient(http);

	private static SpotifyTokenManager Tokens(
		FakeSpotifyOAuthClient? oauth = null,
		TimeSpan? expiresIn = null)
		=> new(Guid.NewGuid(),
			"client-id",
			"client-secret",
			SpotifyTokenSnapshot.FromStored("token",
				"stored-refresh",
				DateTime.UtcNow + (expiresIn ?? TimeSpan.FromHours(1)),
				SpotifyTokenManager.RefreshMargin),
			oauth ?? new FakeSpotifyOAuthClient(),
			new SpotifyTokenPersister(new RecordingIntegrationConfig(), new LoggerConfiguration().CreateLogger()),
			new LoggerConfiguration().CreateLogger(),
			TimeSpan.FromSeconds(5),
			TimeSpan.Zero);

	private static Response Json(HttpStatusCode statusCode, string body)
		=> new(new Dictionary<string, string>())
		{
			StatusCode = statusCode,
			ContentType = "application/json",
			Body = body
		};

	private static APIException ApiException(HttpStatusCode statusCode, string? body = null)
	{
		var response = new Response(new Dictionary<string, string>())
		{
			StatusCode = statusCode,
			Body = body
		};
		return new APIException(response);
	}
}
