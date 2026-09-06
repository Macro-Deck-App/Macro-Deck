using System.Globalization;
using System.Net;
using MacroDeckHost.Integrations.Spotify;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using SpotifyAPI.Web.Http;

namespace MacroDeckHost.Tests.UnitTests.Spotify;

[TestFixture]
internal sealed class SpotifyThrottledHttpClientLoggingTests
{
	private CollectingSink _sink = null!;

	[SetUp]
	public void SetUp()
	{
		_sink = new CollectingSink();
	}

	[Test]
	public async Task Routine_Successful_Polling_Never_Reaches_Information()
	{
		var throttled = Client(_ => Status(HttpStatusCode.OK));

		for (var i = 0; i < 20; i++)
		{
			using (SpotifyRequestScope.Poll())
			{
				await throttled.DoRequest(new StubRequest(), CancellationToken.None);
			}
		}

		Assert.Multiple(() =>
		{
			Assert.That(_sink.Events.Where(logEvent => logEvent.Level >= LogEventLevel.Information), Is.Empty);
			Assert.That(_sink.Events.Count(logEvent => logEvent.Level == LogEventLevel.Debug),
				Is.GreaterThanOrEqualTo(20));
		});
	}

	[Test]
	public async Task A_Rate_Limit_Is_Worth_One_Information_Line_Carrying_Its_Retry_After()
	{
		var throttled = Client(_ => RateLimited("7"));

		using (SpotifyRequestScope.Poll())
		{
			await throttled.DoRequest(new StubRequest(), CancellationToken.None);
		}

		var informational = _sink.Events.Where(logEvent => logEvent.Level >= LogEventLevel.Information).ToList();
		Assert.That(informational, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(informational[0].Level, Is.EqualTo(LogEventLevel.Information));
			Assert.That(Rendered(informational[0]), Does.Contain("429").And.Contain("7"));
		});
	}

	[Test]
	public async Task A_Server_Error_Is_A_Warning()
	{
		var throttled = Client(_ => Status(HttpStatusCode.BadGateway));

		using (SpotifyRequestScope.Poll())
		{
			await throttled.DoRequest(new StubRequest(), CancellationToken.None);
		}

		Assert.That(_sink.Events.Select(logEvent => logEvent.Level), Is.EqualTo(new[] { LogEventLevel.Warning }));
	}

	[Test]
	public async Task A_Poll_The_Limiter_Drops_Stays_At_Debug()
	{
		var throttled = Client(_ => Status(HttpStatusCode.OK), new SpotifyRequestLimiter(burst: 1));

		using (SpotifyRequestScope.Poll())
		{
			Assert.ThrowsAsync<SpotifyThrottledException>(async () =>
				await throttled.DoRequest(new StubRequest(), CancellationToken.None));
		}

		using (SpotifyRequestScope.Interactive())
		{
			await throttled.DoRequest(new StubRequest(), CancellationToken.None);
		}

		Assert.That(_sink.Events.Where(logEvent => logEvent.Level >= LogEventLevel.Information),
			Is.Empty,
			"a dropped poll is routine; the interactive request the reserve exists for went through");
	}

	private SpotifyThrottledHttpClient Client(Func<IRequest, IResponse> handler, SpotifyRequestLimiter? limiter = null)
		=> new(new StubSpotifyHttpClient { Handler = handler },
			limiter ?? new SpotifyRequestLimiter(ceilingPerSecond: 1000, burst: 100),
			new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(_sink).CreateLogger());

	private static string Rendered(LogEvent logEvent)
		=> $"{logEvent.RenderMessage(CultureInfo.InvariantCulture)} {string.Join(' ', logEvent.Properties)}";

	private static Response Status(HttpStatusCode statusCode)
		=> new(new Dictionary<string, string>()) { StatusCode = statusCode, ContentType = "application/json" };

	private static Response RateLimited(string retryAfter)
		=> new(new Dictionary<string, string> { ["Retry-After"] = retryAfter })
		{
			StatusCode = (HttpStatusCode)429,
			ContentType = "application/json"
		};

	private sealed class StubRequest : IRequest
	{
		public Uri BaseAddress { get; } = new("https://api.spotify.com/v1/");

		public Uri Endpoint { get; } = new("me/player", UriKind.Relative);

		public IDictionary<string, string> Headers { get; } = new Dictionary<string, string>();

		public IDictionary<string, string> Parameters { get; } = new Dictionary<string, string>();

		public HttpMethod Method { get; } = HttpMethod.Get;

		public object? Body { get; set; }
	}

	private sealed class CollectingSink : ILogEventSink
	{
		public List<LogEvent> Events { get; } = [];

		public void Emit(LogEvent logEvent) => Events.Add(logEvent);
	}
}
