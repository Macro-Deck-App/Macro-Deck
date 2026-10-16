using MacroDeckHost.Integrations.Twitch.Auth;
using MacroDeckHost.Integrations.Twitch.Protocol;
using Serilog;
using TwitchLib.Api;
using TwitchLib.Api.Core;
using TwitchLib.Api.Core.Enums;
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
