using System.Net;
using System.Text.Json;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.DiscordWebhooks.Actions;
using MacroDeckHost.Integrations.DiscordWebhooks.Configuration;
using MacroDeckHost.Integrations.DiscordWebhooks.Protocol;
using MacroDeckHost.Tests.UnitTests.Delegation;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog.Events;

namespace MacroDeckHost.Tests.UnitTests.DiscordWebhooks;

[TestFixture]
internal sealed class SendMessageActionTests
{
	private readonly List<LogEvent> _logs = [];
	private readonly List<TimeSpan> _delays = [];
	private RecordingIntegrationConfig _config = null!;
	private ScriptedDiscordHandler _discord = null!;
	private HttpClient _http = null!;

	[SetUp]
	public void SetUp()
	{
		_logs.Clear();
		_delays.Clear();
		_config = new RecordingIntegrationConfig();
		_discord = new ScriptedDiscordHandler();
		_http = new HttpClient(_discord, false);
	}

	[TearDown]
	public void TearDown()
	{
		_http.Dispose();
		_discord.Dispose();
	}

	private SendMessageActionDefinition Definition()
	{
		var sender = new DiscordWebhookSender(() => _http,
			(delay, _) =>
			{
				_delays.Add(delay);
				return Task.CompletedTask;
			});

		return new SendMessageActionDefinition(new DiscordWebhookTargets(() => _config),
			sender,
			new FixedTimeProvider(DateTimeOffset.UnixEpoch),
			DiscordWebhookTestSupport.CapturingLogger(_logs));
	}

	private Task<ActionResult> Execute(Dictionary<string, object> parameters)
	{
		return Definition().CreateExecutor().ExecuteAsync(new ActionExecutionContext { Parameters = parameters });
	}

	private static Dictionary<string, object> Message(string content = "hello")
	{
		return new Dictionary<string, object>
		{
			[SendMessageActionDefinition.MessageParameter] =
				new Dictionary<string, string> { ["content"] = content }
		};
	}

	private Guid SaveWebhook(string url = DiscordWebhookTestSupport.Url)
	{
		return _config.AddEntry("Announcements",
			new Dictionary<string, string?>(),
			new Dictionary<string, string> { [DiscordWebhooksConfigKeys.Url] = url });
	}

	[Test]
	public async Task A_saved_webhook_sends_to_its_stored_url()
	{
		var entry = SaveWebhook();
		var parameters = Message();
		parameters[SendMessageActionDefinition.WebhookParameter] = entry.ToString();

		var result = await Execute(parameters);

		using var body = JsonDocument.Parse(_discord.Requests.Single().Body);
		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(_discord.Requests.Single().Uri.AbsoluteUri, Is.EqualTo(DiscordWebhookTestSupport.Url));
			Assert.That(body.RootElement.GetProperty("content").GetString(), Is.EqualTo("hello"));
		});
	}

	[Test]
	public async Task A_custom_webhook_sends_to_the_url_entered_in_the_action()
	{
		var parameters = Message();
		parameters[SendMessageActionDefinition.WebhookParameter] = DiscordWebhookTargets.Custom;
		parameters[SendMessageActionDefinition.WebhookUrlParameter] = DiscordWebhookTestSupport.Url;

		var result = await Execute(parameters);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(_discord.Requests.Single().Uri.AbsoluteUri, Is.EqualTo(DiscordWebhookTestSupport.Url));
		});
	}

	[Test]
	public async Task A_url_that_is_not_a_discord_webhook_is_never_requested()
	{
		var parameters = Message();
		parameters[SendMessageActionDefinition.WebhookUrlParameter] = "https://example.invalid/api/webhooks/1/x";

		var result = await Execute(parameters);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
			Assert.That(_discord.Requests, Is.Empty);
		});
	}

	[Test]
	public async Task A_removed_saved_webhook_fails_with_not_found()
	{
		var parameters = Message();
		parameters[SendMessageActionDefinition.WebhookParameter] = Guid.NewGuid().ToString();

		var result = await Execute(parameters);

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
	}

	[Test]
	public async Task An_invalid_message_is_refused_before_anything_is_sent()
	{
		var parameters = Message(new string('a', 2001));
		parameters[SendMessageActionDefinition.WebhookParameter] = SaveWebhook().ToString();

		var result = await Execute(parameters);

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
			Assert.That(_discord.Requests, Is.Empty);
		});
	}

	[Test]
	public async Task A_short_rate_limit_is_waited_out_once_and_retried()
	{
		_discord.Then(HttpStatusCode.TooManyRequests, """{"retry_after": 1.5, "global": false}""")
			.Then(HttpStatusCode.NoContent);
		var parameters = Message();
		parameters[SendMessageActionDefinition.WebhookParameter] = SaveWebhook().ToString();

		var result = await Execute(parameters);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(_discord.Requests, Has.Count.EqualTo(2));
			Assert.That(_delays, Is.EqualTo(new[] { TimeSpan.FromSeconds(1.5) }));
		});
	}

	[Test]
	public async Task A_long_rate_limit_fails_without_waiting()
	{
		_discord.Then(HttpStatusCode.TooManyRequests,
			string.Empty,
			response => response.Headers.Add("Retry-After", "30"));
		var parameters = Message();
		parameters[SendMessageActionDefinition.WebhookParameter] = SaveWebhook().ToString();

		var result = await Execute(parameters);

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
			Assert.That(_discord.Requests, Has.Count.EqualTo(1));
			Assert.That(_delays, Is.Empty);
		});
	}

	[Test]
	public async Task A_second_rate_limit_after_the_retry_fails()
	{
		_discord.Then(HttpStatusCode.TooManyRequests, """{"retry_after": 0.2}""")
			.Then(HttpStatusCode.TooManyRequests, """{"retry_after": 0.2}""");
		var parameters = Message();
		parameters[SendMessageActionDefinition.WebhookParameter] = SaveWebhook().ToString();

		var result = await Execute(parameters);

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
			Assert.That(_discord.Requests, Has.Count.EqualTo(2));
		});
	}

	[TestCase(HttpStatusCode.BadRequest, ActionErrorCodes.ProviderRejected)]
	[TestCase(HttpStatusCode.Unauthorized, ActionErrorCodes.PermissionDenied)]
	[TestCase(HttpStatusCode.Forbidden, ActionErrorCodes.PermissionDenied)]
	[TestCase(HttpStatusCode.NotFound, ActionErrorCodes.NotFound)]
	[TestCase(HttpStatusCode.InternalServerError, ActionErrorCodes.ProviderError)]
	public async Task Discord_failures_map_to_action_errors(HttpStatusCode status, string code)
	{
		_discord.Then(status,
			"""{"message": "Invalid Form Body", "code": 50035, "errors": {"embeds": {"0": {"title": {"_errors": [{"code": "BASE_TYPE_MAX_LENGTH", "message": "Must be 256 or fewer in length."}]}}}}}""");
		var parameters = Message();
		parameters[SendMessageActionDefinition.WebhookParameter] = SaveWebhook().ToString();

		var result = await Execute(parameters);

		Assert.That(result.ErrorCode, Is.EqualTo(code));
		if (status == HttpStatusCode.BadRequest)
		{
			Assert.That(TestLocalization.Resolve(result.ErrorMessage),
				Does.Contain("embeds.0.title: Must be 256 or fewer in length."));
		}
	}

	[Test]
	public async Task The_webhook_url_never_reaches_the_log()
	{
		_discord.Then(HttpStatusCode.NotFound, """{"message": "Unknown Webhook", "code": 10015}""");
		var parameters = Message();
		parameters[SendMessageActionDefinition.WebhookParameter] = DiscordWebhookTargets.Custom;
		parameters[SendMessageActionDefinition.WebhookUrlParameter] = DiscordWebhookTestSupport.Url;
		await Execute(parameters);

		var saved = Message();
		saved[SendMessageActionDefinition.WebhookParameter] = SaveWebhook().ToString();
		_discord.Then(HttpStatusCode.Unauthorized);
		await Execute(saved);

		var logged = DiscordWebhookTestSupport.Rendered(_logs);
		Assert.Multiple(() =>
		{
			Assert.That(_logs, Is.Not.Empty);
			Assert.That(logged, Does.Not.Contain(DiscordWebhookTestSupport.Token));
			Assert.That(logged, Does.Contain("Announcements"), "a saved webhook is logged by its name");
		});
	}
}
