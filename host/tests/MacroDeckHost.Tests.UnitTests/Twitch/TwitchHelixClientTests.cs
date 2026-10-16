using System.Net;
using MacroDeckHost.Integrations.Twitch.Auth;
using MacroDeckHost.Integrations.Twitch.Protocol;
using Serilog;
using TwitchLib.Api;
using TwitchLib.Api.Core;
using TwitchLib.Api.Core.Enums;
using TwitchLib.Api.Core.Exceptions;
using TwitchLib.Api.Core.Interfaces;

namespace MacroDeckHost.Tests.UnitTests.Twitch;

[TestFixture]
internal sealed class TwitchHelixClientTests
{
	private TwitchTokenPersister _persister = null!;
	private TwitchTokenProvider _tokens = null!;

	[SetUp]
	public void SetUp()
	{
		var logger = new LoggerConfiguration().CreateLogger();
		_persister = new TwitchTokenPersister(new RecordingIntegrationConfig(), logger);
		_tokens = new TwitchTokenProvider(Guid.NewGuid(),
			"client-id",
			FakeTwitchOAuthClient.Tokens("access", "refresh", TimeSpan.FromHours(4)),
			new FakeTwitchOAuthClient(),
			_persister,
			logger);
	}

	[TearDown]
	public void TearDown()
	{
		_tokens.Dispose();
		_persister.Dispose();
	}

	[Test]
	public void A_request_that_times_out_reads_as_a_timeout_instead_of_a_cancellation()
	{
		var client = Client(new TimingOutHttp(() =>
		{
		}));

		Assert.That(async () => await client.GetStreamAsync("111", CancellationToken.None),
			Throws.TypeOf<TimeoutException>());
	}

	[Test]
	public void A_request_the_caller_cancelled_still_reads_as_cancelled()
	{
		using var cancellation = new CancellationTokenSource();
		var client = Client(new TimingOutHttp(cancellation.Cancel));

		Assert.That(async () => await client.GetStreamAsync("111", cancellation.Token),
			Throws.InstanceOf<OperationCanceledException>());
	}

	[TestCase("Bad Request - invalid subscription type and version", TwitchSubscriptionResult.Unsupported)]
	[TestCase("Bad Request - websocket transport session does not exist or has already disconnected",
		TwitchSubscriptionResult.Failed)]
	public async Task A_bad_request_is_unsupported_only_when_twitch_names_the_version(
		string message,
		TwitchSubscriptionResult expected)
	{
		using var response = new HttpResponseMessage(HttpStatusCode.BadRequest);
		var client = Client(new FailingHttp(new BadRequestException(message, response)));

		var result = await Subscribe(client);

		Assert.That(result, Is.EqualTo(expected));
	}

	[TestCase("Gone - The subscription type and version combination has been removed",
		TwitchSubscriptionResult.Unsupported)]
	[TestCase("Conflict - subscription already exists", TwitchSubscriptionResult.Duplicate)]
	public async Task A_removed_version_and_an_existing_subscription_are_told_apart_from_failures(
		string message,
		TwitchSubscriptionResult expected)
	{
		var client = Client(new FailingHttp(new HttpRequestException(message)));

		var result = await Subscribe(client);

		Assert.That(result, Is.EqualTo(expected));
	}

	private static Task<TwitchSubscriptionResult> Subscribe(TwitchHelixClient client)
	{
		return client.CreateEventSubSubscriptionAsync("channel.hype_train.begin",
			"1",
			new Dictionary<string, string>(StringComparer.Ordinal) { ["broadcaster_user_id"] = "111" },
			"session-1",
			CancellationToken.None);
	}

	private TwitchHelixClient Client(IHttpCallHandler http)
	{
		var settings = new ApiSettings
		{
			ClientId = "client-id",
			SkipDynamicScopeValidation = true,
			SkipAutoServerTokenGeneration = true
		};

		var api = new TwitchAPI(null, null, settings, http);

		return new TwitchHelixClient(api, _tokens, new LoggerConfiguration().CreateLogger());
	}

	private sealed class FailingHttp(Exception failure) : IHttpCallHandler
	{
		public Task<KeyValuePair<int, string>> GeneralRequestAsync(
			string url,
			string method,
			string? payload = null,
			ApiVersion api = ApiVersion.Helix,
			string? clientId = null,
			string? accessToken = null)
		{
			return Task.FromException<KeyValuePair<int, string>>(failure);
		}

		public Task PutBytesAsync(string url, byte[] payload)
		{
			throw new NotSupportedException();
		}

		public Task<int> RequestReturnResponseCodeAsync(
			string url,
			string method,
			List<KeyValuePair<string, string>>? getParams = null)
		{
			throw new NotSupportedException();
		}
	}

	private sealed class TimingOutHttp(Action beforeTimeout) : IHttpCallHandler
	{
		public Task<KeyValuePair<int, string>> GeneralRequestAsync(
			string url,
			string method,
			string? payload = null,
			ApiVersion api = ApiVersion.Helix,
			string? clientId = null,
			string? accessToken = null)
		{
			beforeTimeout();
			var timeout = new TaskCanceledException("HttpClient.Timeout elapsed.", new TimeoutException());
			return Task.FromException<KeyValuePair<int, string>>(timeout);
		}

		public Task PutBytesAsync(string url, byte[] payload)
		{
			throw new NotSupportedException();
		}

		public Task<int> RequestReturnResponseCodeAsync(
			string url,
			string method,
			List<KeyValuePair<string, string>>? getParams = null)
		{
			throw new NotSupportedException();
		}
	}
}
