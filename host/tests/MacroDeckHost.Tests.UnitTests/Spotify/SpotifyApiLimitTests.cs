using System.Net;
using MacroDeckHost.Integrations.Spotify;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Issues;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Http;

namespace MacroDeckHost.Tests.UnitTests.Spotify;

[TestFixture]
internal sealed class SpotifyApiLimitTests
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
		""";

	private const string QuotaForbiddenBody =
		"""
		{"error":{"status":403,"message":"Check settings on developer.spotify.com/dashboard, the user may not be registered."}}
		""";

	private const string PremiumForbiddenBody =
		"""{"error":{"status":403,"message":"Player command failed: Premium required"}}""";

	[Test]
	public async Task The_retry_handler_hands_a_429_straight_back()
	{
		var handler = new SpotifyRetryHandler();
		var retries = 0;

		var response = await handler.HandleRetry(new StubRequest(),
			RateLimited("30"),
			(_, _) =>
			{
				retries++;
				return Task.FromResult<IResponse>(RateLimited("30"));
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That((int)response.StatusCode, Is.EqualTo(429));
			Assert.That(retries, Is.Zero);
		});
	}

	[Test]
	public async Task The_retry_handler_hands_a_gateway_error_straight_back()
	{
		var handler = new SpotifyRetryHandler();
		var retries = 0;

		var response = await handler.HandleRetry(new StubRequest(),
			Status(HttpStatusCode.BadGateway),
			(_, _) =>
			{
				retries++;
				return Task.FromResult<IResponse>(Status(HttpStatusCode.OK));
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
			Assert.That(retries, Is.Zero);
		});
	}

	[Test]
	public async Task A_rate_limit_opens_an_episode_and_pauses_reads()
	{
		var http = new StubSpotifyHttpClient { Handler = _ => RateLimited("30") };
		var player = CreatePlayer(http);

		await player.GetStateAsync();
		await player.GetStateAsync();

		Assert.Multiple(() =>
		{
			Assert.That(http.Requests, Has.Count.EqualTo(1));
			Assert.That(player.ApiLimit?.Kind, Is.EqualTo(SpotifyApiLimitKind.RateLimit));
		});
	}

	[Test]
	public async Task A_quota_refusal_opens_a_quota_episode_and_pauses_reads()
	{
		var http = new StubSpotifyHttpClient { Handler = _ => Json(HttpStatusCode.Forbidden, QuotaForbiddenBody) };
		var player = CreatePlayer(http);

		await player.GetStateAsync();
		await player.GetStateAsync();

		Assert.Multiple(() =>
		{
			Assert.That(http.Requests, Has.Count.EqualTo(1));
			Assert.That(player.ApiLimit?.Kind, Is.EqualTo(SpotifyApiLimitKind.Quota));
		});
	}

	[Test]
	public async Task An_ordinary_403_is_not_treated_as_a_quota_refusal()
	{
		var http = new StubSpotifyHttpClient { Handler = _ => Json(HttpStatusCode.Forbidden, PremiumForbiddenBody) };
		var player = CreatePlayer(http);

		await player.GetStateAsync();

		Assert.That(player.ApiLimit, Is.Null);
	}

	[Test]
	public async Task A_limit_episode_closes_once_a_read_succeeds()
	{
		var limited = true;
		var http = new StubSpotifyHttpClient
		{
			Handler = _ => limited ? RateLimited(null) : Json(HttpStatusCode.OK, PlayingTrackJson)
		};
		var player = CreatePlayer(http, rateLimitPause: TimeSpan.Zero);
		await player.GetStateAsync();
		Assert.That(player.ApiLimit, Is.Not.Null);

		limited = false;
		var state = await player.GetStateAsync();

		Assert.Multiple(() =>
		{
			Assert.That(state.IsConnected, Is.True);
			Assert.That(player.ApiLimit, Is.Null);
		});
	}

	[Test]
	public async Task A_stale_state_degrades_to_unavailable_rather_than_disconnected()
	{
		var http = new StubSpotifyHttpClient { Handler = _ => RateLimited("30") };
		var player = CreatePlayer(http, lastStateLifetime: TimeSpan.Zero);

		var state = await player.GetStateAsync();

		Assert.Multiple(() =>
		{
			Assert.That(state.IsConnected, Is.False);
			Assert.That(state.IsUnavailable, Is.True);
			Assert.That(state.StatusMessage, Is.EqualTo("Rate limited by Spotify"));
		});
	}

	[Test]
	public async Task A_quota_episode_names_the_quota_in_the_widget_status()
	{
		var http = new StubSpotifyHttpClient { Handler = _ => Json(HttpStatusCode.Forbidden, QuotaForbiddenBody) };
		var player = CreatePlayer(http, lastStateLifetime: TimeSpan.Zero);

		var state = await player.GetStateAsync();

		Assert.That(state.StatusMessage, Is.EqualTo("Spotify quota reached"));
	}

	[Test]
	public async Task A_state_read_that_never_answers_degrades_instead_of_throwing()
	{
		var player = CreatePlayer(new StallingTransport(),
			lastStateLifetime: TimeSpan.Zero,
			stateReadBudget: TimeSpan.FromMilliseconds(100));

		var state = await player.GetStateAsync();

		Assert.Multiple(() =>
		{
			Assert.That(state.IsUnavailable, Is.True);
			Assert.That(player.UnreachableSince, Is.Not.Null);
		});
	}

	[Test]
	public void The_callers_own_cancellation_is_still_rethrown()
	{
		var player = CreatePlayer(new StallingTransport(), stateReadBudget: TimeSpan.FromMinutes(1));
		using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

		Assert.That(async () => await player.GetStateAsync(caller.Token),
			Throws.InstanceOf<OperationCanceledException>());
	}

	[Test]
	public async Task The_limit_onset_is_logged_once_with_the_remedy()
	{
		var sink = new CollectingSink();
		var http = new StubSpotifyHttpClient { Handler = _ => RateLimited(null) };
		var player = CreatePlayer(http, logger: Collecting(sink), rateLimitPause: TimeSpan.Zero);

		await player.GetStateAsync();
		await player.GetStateAsync();

		Assert.Multiple(() =>
		{
			Assert.That(sink.Events.Count(e =>
					e.Level == LogEventLevel.Warning &&
					e.MessageTemplate.Text.Contains("temporarily closing other apps")),
				Is.EqualTo(1));
			Assert.That(sink.Events.Any(e =>
					e.Level == LogEventLevel.Debug && e.MessageTemplate.Text.Contains("still limiting entry")),
				Is.True);
		});
	}

	[Test]
	public async Task A_recovered_limit_logs_the_episode_end()
	{
		var sink = new CollectingSink();
		var limited = true;
		var http = new StubSpotifyHttpClient
		{
			Handler = _ => limited ? RateLimited(null) : Json(HttpStatusCode.OK, PlayingTrackJson)
		};
		var player = CreatePlayer(http, logger: Collecting(sink), rateLimitPause: TimeSpan.Zero);

		await player.GetStateAsync();
		limited = false;
		await player.GetStateAsync();

		Assert.That(sink.Events.Count(e => e.MessageTemplate.Text.Contains("stopped limiting entry")),
			Is.EqualTo(1));
	}

	[Test]
	public async Task A_rate_limited_command_also_opens_the_episode()
	{
		var http = new StubSpotifyHttpClient { Handler = _ => RateLimited("30") };
		var player = CreatePlayer(http);

		await player.NextAsync();
		var requestsAtLimit = http.Requests.Count;
		await player.TogglePlayPauseAsync();
		await player.PreviousAsync();

		Assert.Multiple(() =>
		{
			Assert.That(player.ApiLimit?.Kind, Is.EqualTo(SpotifyApiLimitKind.RateLimit));
			Assert.That(http.Requests, Has.Count.EqualTo(requestsAtLimit));
		});
	}

	[Test]
	public async Task A_rate_limited_account_surfaces_the_api_limit_issue()
	{
		var integration = new SpotifyIntegration(new FakeSpotifyOAuthClient(), new LimitedTransport());
		var config = new SpotifyConfigStub();
		await integration.InitializeAsync(new SpotifyContextStub(config));

		await integration.GetPlayer(config.EntryId.ToString())!.GetStateAsync();
		var issues = await integration.GetIssuesAsync();

		Assert.Multiple(() =>
		{
			Assert.That(issues, Has.Count.EqualTo(1));
			Assert.That(issues[0].Id, Is.EqualTo("api-limited"));
			Assert.That(issues[0].Severity, Is.EqualTo(IntegrationIssueSeverity.Warning));
			// No button: re-authorizing cannot lift a rate limit, so the remedy lives in the description.
			Assert.That(TestLocalization.Resolve(issues[0].ActionLabel), Is.Null);
			Assert.That(TestLocalization.Resolve(issues[0].Description),
				Does.Contain("other apps on this Spotify account"));
		});

		await integration.ShutdownAsync();
	}

	[Test]
	public async Task A_quota_refusal_surfaces_the_quota_wording()
	{
		var integration = new SpotifyIntegration(new FakeSpotifyOAuthClient(), new LimitedTransport { Quota = true });
		var config = new SpotifyConfigStub();
		await integration.InitializeAsync(new SpotifyContextStub(config));

		await integration.GetPlayer(config.EntryId.ToString())!.GetStateAsync();
		var issues = await integration.GetIssuesAsync();

		Assert.Multiple(() =>
		{
			Assert.That(issues, Has.Count.EqualTo(1));
			Assert.That(TestLocalization.Resolve(issues[0].Title), Is.EqualTo("Spotify quota reached"));
			Assert.That(TestLocalization.Resolve(issues[0].Description), Does.Contain("used up its request quota"));
		});

		await integration.ShutdownAsync();
	}

	[Test]
	public async Task The_api_limit_issue_text_is_stable_across_polls()
	{
		var integration = new SpotifyIntegration(new FakeSpotifyOAuthClient(), new LimitedTransport());
		var config = new SpotifyConfigStub();
		await integration.InitializeAsync(new SpotifyContextStub(config));
		var player = integration.GetPlayer(config.EntryId.ToString())!;

		await player.GetStateAsync();
		var first = await integration.GetIssuesAsync();
		await player.GetStateAsync();
		var second = await integration.GetIssuesAsync();

		Assert.That(second[0].Description, Is.EqualTo(first[0].Description));

		await integration.ShutdownAsync();
	}

	[Test]
	public async Task A_retry_after_header_is_read_as_seconds()
	{
		var http = new StubSpotifyHttpClient { Handler = _ => RateLimited("30") };
		var player = CreatePlayer(http);

		await player.GetStateAsync();

		var pause = player.ApiLimit!.PausedUntil - DateTimeOffset.UtcNow;
		Assert.That(pause, Is.EqualTo(TimeSpan.FromSeconds(31)).Within(TimeSpan.FromSeconds(2)));
	}

	[Test]
	public async Task A_rate_limited_liked_check_opens_the_episode_instead_of_a_debug_line()
	{
		var limited = false;
		var http = new StubSpotifyHttpClient
		{
			Handler = _ => limited ? RateLimited("30") : Json(HttpStatusCode.OK, PlayingTrackJson)
		};
		var player = CreatePlayer(http);
		await player.GetStateAsync();

		limited = true;
		await player.IsCurrentItemSavedAsync();
		var afterFirst = http.Requests.Count;
		await player.IsCurrentItemSavedAsync();
		await player.IsCurrentItemSavedAsync();

		Assert.Multiple(() =>
		{
			Assert.That(player.ApiLimit?.Kind, Is.EqualTo(SpotifyApiLimitKind.RateLimit));
			Assert.That(http.Requests, Has.Count.EqualTo(afterFirst));
		});
	}

	[Test]
	public async Task A_failing_liked_check_backs_off_instead_of_re_asking_every_poll()
	{
		var failing = false;
		var http = new StubSpotifyHttpClient
		{
			Handler = _ => failing
				? Status(HttpStatusCode.InternalServerError)
				: Json(HttpStatusCode.OK, PlayingTrackJson)
		};
		var player = CreatePlayer(http);
		await player.GetStateAsync();

		failing = true;
		await player.IsCurrentItemSavedAsync();
		var afterFirst = http.Requests.Count;
		await player.IsCurrentItemSavedAsync();

		Assert.That(http.Requests, Has.Count.EqualTo(afterFirst));
	}

	[Test]
	public async Task A_stored_limit_keeps_reads_paused_across_a_restart()
	{
		var http = new StubSpotifyHttpClient { Handler = _ => Json(HttpStatusCode.OK, PlayingTrackJson) };
		var player = CreatePlayer(http,
			openLimit: new SpotifyApiLimitStatus(SpotifyApiLimitKind.RateLimit,
				DateTimeOffset.UtcNow.AddMinutes(-5),
				DateTimeOffset.UtcNow.AddHours(2)));

		await player.GetStateAsync();

		Assert.Multiple(() =>
		{
			Assert.That(http.Requests, Is.Empty);
			Assert.That(player.ApiLimit?.Kind, Is.EqualTo(SpotifyApiLimitKind.RateLimit));
		});
	}

	[Test]
	public async Task An_expired_stored_limit_does_not_pause_anything()
	{
		var http = new StubSpotifyHttpClient { Handler = _ => Json(HttpStatusCode.OK, PlayingTrackJson) };
		var player = CreatePlayer(http,
			openLimit: new SpotifyApiLimitStatus(SpotifyApiLimitKind.RateLimit,
				DateTimeOffset.UtcNow.AddHours(-2),
				DateTimeOffset.UtcNow.AddSeconds(-1)));

		var state = await player.GetStateAsync();

		Assert.That(state.IsConnected, Is.True);
	}

	[Test]
	public async Task A_new_limit_is_persisted()
	{
		var config = new RecordingIntegrationConfig();
		var store = new SpotifyApiLimitStore(config, new LoggerConfiguration().CreateLogger());
		var http = new StubSpotifyHttpClient { Handler = _ => RateLimited("30") };
		var player = CreatePlayer(http, limitStore: store);

		await player.GetStateAsync();
		await store.FlushAsync();

		Assert.That(config.Strings[SpotifyConfigKeys.ApiLimitUntil], Is.Not.Null);
	}

	[Test]
	public async Task A_command_is_not_sent_while_the_pause_is_open()
	{
		var http = new StubSpotifyHttpClient { Handler = _ => RateLimited("30") };
		var player = CreatePlayer(http);
		await player.GetStateAsync();
		var afterLimit = http.Requests.Count;

		await player.NextAsync();
		await player.PauseAsync();

		Assert.That(http.Requests, Has.Count.EqualTo(afterLimit));
	}

	[Test]
	public async Task The_device_picker_refuses_while_the_pause_is_open()
	{
		var http = new StubSpotifyHttpClient { Handler = _ => RateLimited("30") };
		var player = CreatePlayer(http);
		await player.GetStateAsync();
		var afterLimit = http.Requests.Count;

		Assert.That(async () => await player.GetDevicesAsync(CancellationToken.None),
			Throws.InstanceOf<SpotifyThrottledException>());
		Assert.That(http.Requests, Has.Count.EqualTo(afterLimit));
	}

	[Test]
	public async Task A_rate_limited_token_endpoint_pauses_the_player()
	{
		// The first call after a screen unlock is always a refresh, because the access token expired
		// while the machine was locked. A 429 there used to retry on a flat 5s backoff and told the
		// player nothing, so the poll kept asking for a token it could not get.
		var oauth = new FakeSpotifyOAuthClient();
		oauth.Results.Enqueue(FakeSpotifyOAuthClient.RateLimited(TimeSpan.FromMinutes(3)));
		var http = new StubSpotifyHttpClient { Handler = _ => Json(HttpStatusCode.OK, PlayingTrackJson) };
		var player = CreatePlayer(http, tokens: Tokens(oauth, expiresIn: TimeSpan.FromHours(-1)));

		await player.GetStateAsync();

		Assert.Multiple(() =>
		{
			Assert.That(player.ApiLimit?.Kind, Is.EqualTo(SpotifyApiLimitKind.RateLimit));
			Assert.That(player.ApiLimit?.PausedUntil,
				Is.EqualTo(DateTimeOffset.UtcNow.AddMinutes(3)).Within(TimeSpan.FromSeconds(5)));
			Assert.That(http.Requests, Is.Empty);
		});
	}

	[Test]
	public async Task The_local_limiter_drops_a_poll_rather_than_earning_a_429()
	{
		var http = new StubSpotifyHttpClient { Handler = _ => Json(HttpStatusCode.OK, PlayingTrackJson) };
		var throttled = new SpotifyThrottledHttpClient(http, new SpotifyRequestLimiter(burst: 1));
		var player = CreatePlayer(throttled);

		var state = await player.GetStateAsync();

		Assert.Multiple(() =>
		{
			Assert.That(http.Requests, Is.Empty);
			Assert.That(state.IsConnected, Is.False);
			Assert.That(player.ApiLimit, Is.Null);
		});
	}

	[Test]
	public async Task Request_diagnostics_are_semantic_and_do_not_include_request_secrets()
	{
		const string poison = "secret-search-and-token";
		var sink = new CollectingSink();
		var request = new StubRequest
		{
			Endpoint = new Uri($"search?q={poison}&playlist=private-id", UriKind.Relative),
			Headers = new Dictionary<string, string> { ["Authorization"] = poison },
			Parameters = new Dictionary<string, string> { ["q"] = poison },
			Body = poison
		};
		var http = new StubSpotifyHttpClient { Handler = _ => RateLimited("7") };
		var throttled = new SpotifyThrottledHttpClient(http,
			new SpotifyRequestLimiter(burst: 10),
			Collecting(sink));

		using (SpotifyRequestScope.Interactive("catalog-search", "setup"))
		{
			await throttled.DoRequest(request, CancellationToken.None);
		}

		var rendered = string.Join('\n',
			sink.Events.Select(logEvent =>
				$"{logEvent.RenderMessage(global::System.Globalization.CultureInfo.InvariantCulture)} " +
				$"{string.Join(' ', logEvent.Properties)}"));
		Assert.Multiple(() =>
		{
			Assert.That(sink.Events, Has.Some.Property("Level").EqualTo(LogEventLevel.Information));
			Assert.That(rendered, Does.Contain("catalog/search"));
			Assert.That(rendered, Does.Contain("catalog-search"));
			Assert.That(rendered, Does.Contain("setup"));
			Assert.That(rendered, Does.Contain("429"));
			Assert.That(rendered, Does.Contain("7"));
			Assert.That(rendered, Does.Not.Contain(poison));
			Assert.That(rendered, Does.Not.Contain("private-id"));
		});
	}

	private static SpotifyMusicPlayer CreatePlayer(
		IHTTPClient http,
		ILogger? logger = null,
		TimeSpan? lastStateLifetime = null,
		TimeSpan? stateReadBudget = null,
		TimeSpan? rateLimitPause = null,
		SpotifyApiLimitStore? limitStore = null,
		SpotifyApiLimitStatus? openLimit = null,
		SpotifyTokenManager? tokens = null)
	{
		tokens ??= Tokens();
		var player = new SpotifyMusicPlayer(logger,
			lastStateLifetime: lastStateLifetime,
			stateReadBudget: stateReadBudget,
			rateLimitPause: rateLimitPause,
			limitStore: limitStore);
		player.Connect(Config(http, tokens), tokens, openLimit);
		return player;
	}

	private static SpotifyClientConfig Config(IHTTPClient http, ISpotifyAccessTokenSource tokens)
		=> SpotifyClientConfig.CreateDefault()
			.WithAuthenticator(new SpotifyAccessTokenAuthenticator(tokens))
			.WithHTTPClient(http)
			.WithRetryHandler(new SpotifyRetryHandler());

	private static SpotifyTokenManager Tokens(ISpotifyOAuthClient? oauth = null, TimeSpan? expiresIn = null)
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

	private static Response RateLimited(string? retryAfter)
		=> new(retryAfter is null
			? new Dictionary<string, string>()
			: new Dictionary<string, string> { ["Retry-After"] = retryAfter })
		{
			StatusCode = (HttpStatusCode)429,
			ContentType = "application/json",
			Body = """{"error":{"status":429,"message":"rate limited"}}"""
		};

	private static Response Status(HttpStatusCode statusCode)
		=> new(new Dictionary<string, string>()) { StatusCode = statusCode, ContentType = "application/json" };

	private static Response Json(HttpStatusCode statusCode, string body)
		=> new(new Dictionary<string, string>())
		{
			StatusCode = statusCode,
			ContentType = "application/json",
			Body = body
		};

	private static Logger Collecting(CollectingSink sink)
		=> new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();

	private sealed class CollectingSink : ILogEventSink
	{
		public List<LogEvent> Events { get; } = [];

		public void Emit(LogEvent logEvent) => Events.Add(logEvent);
	}

	private sealed class LimitedTransport : IHTTPClient
	{
		public bool Quota { get; init; }

		public Task<IResponse> DoRequest(IRequest request, CancellationToken cancel)
			=> Task.FromResult<IResponse>(Quota
				? Json(HttpStatusCode.Forbidden, QuotaForbiddenBody)
				: RateLimited("30"));

		public void SetRequestTimeout(TimeSpan timeout)
		{
		}

		public void Dispose()
		{
		}
	}

	private sealed class StallingTransport : IHTTPClient
	{
		public async Task<IResponse> DoRequest(IRequest request, CancellationToken cancel)
		{
			await Task.Delay(Timeout.Infinite, cancel);
			throw new InvalidOperationException("unreachable");
		}

		public void SetRequestTimeout(TimeSpan timeout)
		{
		}

		public void Dispose()
		{
		}
	}

	private sealed class StubRequest : IRequest
	{
		public Uri BaseAddress { get; } = new("https://api.spotify.com/v1/");

		public Uri Endpoint { get; init; } = new("me/player", UriKind.Relative);

		public IDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();

		public IDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();

		public HttpMethod Method { get; } = HttpMethod.Get;

		public object? Body { get; set; }
	}
}
