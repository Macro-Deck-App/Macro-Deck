using System.Globalization;
using System.Net;
using System.Text.Json;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Integrations.LiveTennis;

namespace MacroDeckHost.Tests.UnitTests.LiveTennis;

[TestFixture]
internal sealed class LiveTennisTests
{
	private const string _match = """
		{"players":{"p1":{"name":"Nadal"},"p2":{"name":"Federer"}},
		 "score":{"games":[[6,3],[4,4]],"points":["30","15"],"is_tiebreak":false}}
		""";

	[Test]
	public async Task Setup_stores_the_key_as_a_secret_without_making_a_request()
	{
		var flow = new LiveTennisConfigFlow();
		var context = new TestFlowContext();
		var start = await flow.StartAsync(context, CancellationToken.None);
		var result = await flow.SubmitAsync("connection",
			new Dictionary<string, object?> { ["apiKey"] = " fixture-key " }, context, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(start.NextStep!.Fields.Single().Type, Is.EqualTo(ActionParameterType.Secret));
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(result.Values!["apiKey"].IsSecret, Is.True);
			Assert.That(result.Values["apiKey"].Value, Is.EqualTo("fixture-key"));
			Assert.That(result.Values["lastAttempt"].Value, Is.Null);
		});
	}

	[TestCase("")]
	[TestCase("   ")]
	[TestCase("key\r\ninjected")]
	public async Task Invalid_keys_leave_setup_incomplete(string key)
	{
		var result = await new LiveTennisConfigFlow().SubmitAsync("connection",
			new Dictionary<string, object?> { ["apiKey"] = key }, new TestFlowContext(), CancellationToken.None);
		Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
	}

	[Test]
	public async Task All_variables_share_one_persisted_attempt_every_fifteen_minutes()
	{
		var clock = new TestClock();
		var config = new TestConfig();
		using var handler = new TestHandler();
		using var integration = new LiveTennisIntegration(new HttpClient(handler), clock);
		await integration.InitializeAsync(new TestContext(config));

		Assert.That((await integration.ReadAsync("tennis_scores")).Value, Is.Null);
		clock.Advance(899);
		Assert.That((await integration.ReadAsync("tennis_match_count")).Value, Is.Null);
		Assert.That(handler.Requests, Is.Zero);
		clock.Advance(1);
		var values = await Task.WhenAll(Enumerable.Range(0, 20)
			.Select(_ => integration.ReadAsync("tennis_scores").AsTask()));
		Assert.That(values.Select(v => v.Value), Is.All.EqualTo("Nadal - Federer: 6-4 3-4 (30-15)"));
		Assert.That((await integration.ReadAsync("tennis_match_count")).Value, Is.EqualTo(1));
		Assert.That((await integration.ReadAsync("tennis_updated_at")).Value, Is.EqualTo("2026-09-22 00:15:00Z"));
		Assert.That(handler.Requests, Is.EqualTo(1));
		Assert.That(handler.LastUri, Is.EqualTo("https://api.livetennisapi.com/api/public/v1/matches?status=live&limit=500"));
		Assert.That(handler.LastKey, Is.EqualTo("fixture-key"));
		Assert.That(config.LastAttempt, Is.EqualTo(clock.GetUtcNow().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)));

		await integration.ShutdownAsync();
		using var restarted = new LiveTennisIntegration(new HttpClient(handler), clock);
		await restarted.InitializeAsync(new TestContext(config));
		Assert.That((await restarted.ReadAsync("tennis_scores")).Value, Is.Null);
		clock.Advance(899);
		await restarted.ReadAsync("tennis_scores");
		Assert.That(handler.Requests, Is.EqualTo(1));
		clock.Advance(1);
		await restarted.ReadAsync("tennis_scores");
		Assert.That(handler.Requests, Is.EqualTo(2));
		await restarted.ShutdownAsync();
	}

	[TestCase(HttpStatusCode.Unauthorized)]
	[TestCase(HttpStatusCode.TooManyRequests)]
	[TestCase(HttpStatusCode.ServiceUnavailable)]
	public async Task Failed_requests_consume_the_same_budget_and_clear_stale_scores(HttpStatusCode status)
	{
		var clock = new TestClock();
		var config = new TestConfig { LastAttempt = "0" };
		using var handler = new TestHandler();
		using var integration = new LiveTennisIntegration(new HttpClient(handler), clock);
		await integration.InitializeAsync(new TestContext(config));
		Assert.That((await integration.ReadAsync("tennis_scores")).Value, Is.Not.Null);
		clock.Advance(900);
		handler.Status = status;
		Assert.That((await integration.ReadAsync("tennis_scores")).Value, Is.Null);
		handler.Status = HttpStatusCode.OK;
		clock.Advance(899);
		Assert.That((await integration.ReadAsync("tennis_scores")).Value, Is.Null);
		Assert.That(handler.Requests, Is.EqualTo(2));
		clock.Advance(1);
		Assert.That((await integration.ReadAsync("tennis_scores")).Value, Is.Not.Null);
		Assert.That(handler.Requests, Is.EqualTo(3));
		await integration.ShutdownAsync();
	}

	[TestCase(null)]
	[TestCase("broken")]
	[TestCase("-9223372036854775808")]
	[TestCase("9223372036854775807")]
	public async Task Missing_corrupt_or_future_state_starts_a_full_cooldown(string? state)
	{
		var clock = new TestClock();
		var config = new TestConfig { LastAttempt = state };
		using var handler = new TestHandler();
		using var integration = new LiveTennisIntegration(new HttpClient(handler), clock);
		await integration.InitializeAsync(new TestContext(config));
		await integration.ReadAsync("tennis_scores");
		clock.Advance(899);
		await integration.ReadAsync("tennis_scores");
		Assert.That(handler.Requests, Is.Zero);
		clock.Advance(1);
		await integration.ReadAsync("tennis_scores");
		Assert.That(handler.Requests, Is.EqualTo(1));
		await integration.ShutdownAsync();
	}

	[Test]
	public async Task Failed_quota_persistence_prevents_network_access()
	{
		var config = new TestConfig { LastAttempt = "0", FailWrites = true };
		using var handler = new TestHandler();
		using var integration = new LiveTennisIntegration(new HttpClient(handler), new TestClock());
		await integration.InitializeAsync(new TestContext(config));
		Assert.That((await integration.ReadAsync("tennis_scores")).Value, Is.Null);
		Assert.That(handler.Requests, Is.Zero);
		await integration.ShutdownAsync();
	}

	[TestCase("{}")]
	[TestCase("{\"data\":[{}]}")]
	[TestCase("not json")]
	public async Task Malformed_responses_are_unavailable_and_are_not_retried(string body)
	{
		using var handler = new TestHandler { Body = body };
		using var integration = new LiveTennisIntegration(new HttpClient(handler), new TestClock());
		await integration.InitializeAsync(new TestContext(new TestConfig { LastAttempt = "0" }));
		Assert.That((await integration.ReadAsync("tennis_scores")).Value, Is.Null);
		Assert.That((await integration.ReadAsync("tennis_match_count")).Value, Is.Null);
		Assert.That(handler.Requests, Is.EqualTo(1));
		await integration.ShutdownAsync();
	}

	[Test]
	public async Task Shutdown_cancels_an_inflight_request_without_releasing_its_quota()
	{
		var config = new TestConfig { LastAttempt = "0" };
		var clock = new TestClock();
		using var handler = new TestHandler { WaitForCancellation = true };
		using var integration = new LiveTennisIntegration(new HttpClient(handler), clock);
		await integration.InitializeAsync(new TestContext(config));
		var read = integration.ReadAsync("tennis_scores").AsTask();
		await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await integration.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
		Assert.That((await read).Value, Is.Null);
		Assert.That(config.LastAttempt, Is.EqualTo(clock.GetUtcNow().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)));
		Assert.That((await integration.ReadAsync("tennis_scores")).Value, Is.Null);
		Assert.That(handler.Requests, Is.EqualTo(1));
	}

	[Test]
	public async Task Disabled_or_unconfigured_integrations_do_not_call_the_service()
	{
		using var handler = new TestHandler();
		using var integration = new LiveTennisIntegration(new HttpClient(handler), new TestClock());
		Assert.That((await integration.ReadAsync("tennis_scores")).Value, Is.Null);
		await integration.InitializeAsync(new TestContext(new TestConfig { Key = null }));
		Assert.That((await integration.ReadAsync("tennis_scores")).Value, Is.Null);
		Assert.That(handler.Requests, Is.Zero);
		await integration.ShutdownAsync();
	}

	[TestCase("null", "Nadal - Federer: ?")]
	[TestCase("{\"games\":null}", "Nadal - Federer: ?")]
	[TestCase("{\"games\":[[],[]],\"points\":[null,null]}", "Nadal - Federer: ?")]
	[TestCase("{\"games\":[[6],[4]],\"points\":[null,null]}", "Nadal - Federer: 6-4")]
	[TestCase("{\"games\":[[6,4,10],[4,6,5]],\"points\":[\"10\",\"5\"],\"is_tiebreak\":true}",
		"Nadal - Federer: 6-4 4-6 [10-5] (10-5)")]
	[TestCase("{\"games\":[[6],[6]],\"points\":[\"3\",\"2\"],\"is_tiebreak\":true}",
		"Nadal - Federer: [6-6] (3-2)")]
	public void Scores_preserve_player_major_order_missing_values_and_tiebreaks(string score, string expected)
	{
		using var json = JsonDocument.Parse("{\"data\":[{\"players\":{\"p1\":{\"name\":\"Nadal\"}," +
			"\"p2\":{\"name\":\"Federer\"}},\"score\":" + score + "}]}");
		Assert.That(LiveTennisSnapshot.Parse(json.RootElement, DateTimeOffset.UnixEpoch).Scores, Is.EqualTo(expected));
	}

	[Test]
	public void Empty_snapshots_are_distinct_from_unavailable_data_and_summaries_are_bounded()
	{
		using var empty = JsonDocument.Parse("{\"data\":[]}");
		var snapshot = LiveTennisSnapshot.Parse(empty.RootElement, DateTimeOffset.UnixEpoch);
		Assert.That(snapshot.MatchCount, Is.Zero);
		Assert.That(snapshot.Scores, Is.Empty);
		using var six = JsonDocument.Parse("{\"data\":[" + string.Join(',', Enumerable.Repeat(_match, 6)) + "]}");
		snapshot = LiveTennisSnapshot.Parse(six.RootElement, DateTimeOffset.UnixEpoch);
		Assert.That(snapshot.MatchCount, Is.EqualTo(6));
		Assert.That(snapshot.Scores.Split('\n'), Has.Length.EqualTo(5));
	}

	private sealed class TestClock : TimeProvider
	{
		private DateTimeOffset _now = new(2026, 9, 22, 0, 0, 0, TimeSpan.Zero);
		public override DateTimeOffset GetUtcNow() => _now;
		public void Advance(int seconds) => _now = _now.AddSeconds(seconds);
	}

	private sealed class TestFlowContext : IConfigFlowContext
	{
		public IOAuthSession OAuth => throw new NotSupportedException();
	}

	private sealed class TestHandler : HttpMessageHandler
	{
		public int Requests { get; private set; }
		public string? LastUri { get; private set; }
		public string? LastKey { get; private set; }
		public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
		public string Body { get; set; } = "{\"data\":[" + _match + "]}";
		public bool WaitForCancellation { get; init; }
		public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Requests++;
			LastUri = request.RequestUri!.AbsoluteUri;
			LastKey = request.Headers.GetValues("X-API-Key").Single();
			Started.TrySetResult();
			await Task.Delay(WaitForCancellation ? Timeout.Infinite : 10, cancellationToken);
			return new HttpResponseMessage(Status) { Content = new StringContent(Body) };
		}
	}

	private sealed class TestConfig : IIntegrationConfig
	{
		public string? LastAttempt { get; set; }
		public string? Key { get; set; } = "fixture-key";
		public bool FailWrites { get; init; }
		public Task<IReadOnlyList<ConfigEntrySnapshot>> GetEntriesAsync(CancellationToken cancellationToken = default)
			=> Task.FromResult<IReadOnlyList<ConfigEntrySnapshot>>([new(Guid.Empty, "Tennis")]);
		public Task<string?> GetStringAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
			=> Task.FromResult(LastAttempt);
		public Task<string?> GetSecretAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
			=> Task.FromResult(Key);
		public Task SetStringAsync(Guid entryId, string key, string? value, CancellationToken cancellationToken = default)
		{
			if (FailWrites)
			{
				throw new IOException("Quota storage unavailable");
			}

			LastAttempt = value;
			return Task.CompletedTask;
		}
		public Task SetSecretAsync(Guid entryId, string key, string value, CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();
	}

	private sealed class TestContext(TestConfig config) : IIntegrationContext
	{
		public IIntegrationConfig Config => config;
		public IVariableApi Variables => throw new NotSupportedException();
		public IUserVariableApi UserVariables => throw new NotSupportedException();
		public IDeckNavigator Deck => throw new NotSupportedException();
		public IScriptApi Scripts => throw new NotSupportedException();
		public IWidgetApi Widgets => throw new NotSupportedException();
		public IEventPublisher Events => throw new NotSupportedException();
		public IUserNotifier Notifications => throw new NotSupportedException();
	}
}
