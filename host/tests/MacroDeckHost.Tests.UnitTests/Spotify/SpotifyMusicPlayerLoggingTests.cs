using System.Globalization;
using System.Net;
using MacroDeckHost.Integrations.Spotify;
using MacroDeckHost.Tests.UnitTests.Auth;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Http;

namespace MacroDeckHost.Tests.UnitTests.Spotify;

[TestFixture]
internal sealed class SpotifyMusicPlayerLoggingTests
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

	[Test]
	public async Task A_transient_episode_logs_one_warning_onset_then_debug()
	{
		var sink = new CollectingSink();
		var oauth = new FakeSpotifyOAuthClient();
		oauth.Results.Enqueue(FakeSpotifyOAuthClient.Unreachable());
		oauth.Results.Enqueue(FakeSpotifyOAuthClient.Unreachable());
		var http = new StubSpotifyHttpClient { Handler = _ => Json(HttpStatusCode.NoContent, string.Empty) };
		var player = CreatePlayerWithExpiredToken(oauth, http, Collecting(sink));

		await player.GetStateAsync();
		await player.GetStateAsync();

		Assert.Multiple(() =>
		{
			Assert.That(sink.Events.Count(e =>
					e.Level == LogEventLevel.Warning && e.MessageTemplate.Text.Contains("started failing")),
				Is.EqualTo(1));
			Assert.That(sink.Events.Count(e =>
					e.Level == LogEventLevel.Debug && e.MessageTemplate.Text.Contains("keeping last state")),
				Is.EqualTo(1));
			Assert.That(player.UnreachableSince, Is.Not.Null);
		});
	}

	[Test]
	public async Task A_recovered_state_read_logs_the_episode_end()
	{
		var sink = new CollectingSink();
		var oauth = new FakeSpotifyOAuthClient();
		oauth.Results.Enqueue(FakeSpotifyOAuthClient.Unreachable());
		var http = new StubSpotifyHttpClient
		{
			Handler = request => IsDevicesRequest(request)
				? Json(HttpStatusCode.OK, """{"devices":[]}""")
				: Json(HttpStatusCode.NoContent, string.Empty)
		};
		var player = CreatePlayerWithExpiredToken(oauth, http, Collecting(sink));

		await player.GetStateAsync();
		await player.GetStateAsync();

		Assert.Multiple(() =>
		{
			Assert.That(sink.Events.Count(e =>
					e.Level == LogEventLevel.Information &&
					e.MessageTemplate.Text.Contains("state reads for entry {EntryId} recovered")),
				Is.EqualTo(1));
			Assert.That(player.UnreachableSince, Is.Null);
		});
	}

	[Test]
	public async Task A_poll_with_no_client_logs_once_per_episode()
	{
		var sink = new CollectingSink();
		var player = new SpotifyMusicPlayer(Collecting(sink));

		await player.GetStateAsync();
		await player.GetStateAsync();

		Assert.That(sink.Events.Count(e => e.MessageTemplate.Text.Contains("no connected client")),
			Is.EqualTo(1));
	}

	[Test]
	public async Task Connect_rearms_the_no_client_log()
	{
		var sink = new CollectingSink();
		var player = new SpotifyMusicPlayer(Collecting(sink));
		await player.GetStateAsync();

		var tokens = Tokens();
		var http = new StubSpotifyHttpClient { Handler = _ => Json(HttpStatusCode.NoContent, string.Empty) };
		player.Connect(Config(http, tokens), tokens);
		player.Disconnect();
		await player.GetStateAsync();

		Assert.That(sink.Events.Count(e => e.MessageTemplate.Text.Contains("no connected client")),
			Is.EqualTo(2));
	}

	[Test]
	public async Task A_connection_flip_logs_the_direction_and_reason()
	{
		var sink = new CollectingSink();
		var unauthorized = false;
		var http = new StubSpotifyHttpClient
		{
			Handler = _ => unauthorized
				? Json(HttpStatusCode.Unauthorized, """{"error":{"status":401,"message":"Access token missing"}}""")
				: Json(HttpStatusCode.OK, PlayingTrackJson)
		};
		var time = new ManualTimeProvider();
		var player = CreatePlayer(http, Collecting(sink), time);

		await player.GetStateAsync();
		unauthorized = true;
		time.Advance(SpotifyPollSchedule.PausedInterval);
		await player.GetStateAsync();
		time.Advance(SpotifyPollSchedule.PausedInterval);
		await player.GetStateAsync();

		var flips = sink.Events
			.Where(e => e.MessageTemplate.Text.Contains("{Previous} -> {Current}"))
			.ToList();
		Assert.Multiple(() =>
		{
			Assert.That(flips, Has.Count.EqualTo(2));
			Assert.That(Reason(flips[0]), Is.EqualTo("state read succeeded"));
			Assert.That(Reason(flips[1]), Is.EqualTo("access token rejected (401)"));
		});
	}

	[Test]
	public async Task An_invalid_playlist_argument_is_not_written_to_logs()
	{
		const string poison = "secret-query-value";
		var sink = new CollectingSink();
		var http = new StubSpotifyHttpClient
		{
			Handler = _ => Json(HttpStatusCode.OK, PlayingTrackJson)
		};
		var player = CreatePlayer(http, Collecting(sink));

		await player.GetStateAsync();
		await player.ChangePlaylistMembershipAsync($"https://example.invalid/?token={poison}", "add");

		Assert.That(sink.Events.Select(logEvent => logEvent.RenderMessage(CultureInfo.InvariantCulture)),
			Has.None.Contains(poison));
		player.Dispose();
	}

	private static string Reason(LogEvent logEvent)
		=> ((ScalarValue)logEvent.Properties["Reason"]).Value as string ?? string.Empty;

	private static bool IsDevicesRequest(IRequest request)
		=> request.Endpoint.ToString().Contains("devices", StringComparison.Ordinal);

	private static SpotifyMusicPlayer CreatePlayer(
		StubSpotifyHttpClient http,
		ILogger logger,
		TimeProvider? timeProvider = null)
	{
		var tokens = Tokens();
		var player = new SpotifyMusicPlayer(logger, timeProvider: timeProvider);
		player.Connect(Config(http, tokens), tokens);
		return player;
	}

	private static SpotifyMusicPlayer CreatePlayerWithExpiredToken(
		FakeSpotifyOAuthClient oauth,
		StubSpotifyHttpClient http,
		ILogger logger)
	{
		var tokens = Tokens(oauth, TimeSpan.Zero);
		var player = new SpotifyMusicPlayer(logger);
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

	private static Logger Collecting(CollectingSink sink)
		=> new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();

	private sealed class CollectingSink : ILogEventSink
	{
		public List<LogEvent> Events { get; } = [];

		public void Emit(LogEvent logEvent) => Events.Add(logEvent);
	}
}
