using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Integrations.Discord;
using MacroDeckHost.Integrations.Discord.Rpc;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.ConfigFlow;

namespace MacroDeckHost.Tests.UnitTests.Discord;

[TestFixture]
internal sealed class DiscordConfigFlowTests
{
	private static readonly string[] _credentialFields = ["clientId", "clientSecret"];

	private FakeDiscordRpcClient _client = null!;
	private FakeDiscordOAuthClient _oauth = null!;
	private DiscordConfigFlow _flow = null!;

	[SetUp]
	public void SetUp()
	{
		_client = new FakeDiscordRpcClient()
			.Responds("AUTHORIZE", """{"code":"auth-code"}""")
			.Responds("AUTHENTICATE", """{"user":{"id":"me","global_name":"Tester"}}""");
		_oauth = new FakeDiscordOAuthClient();
		_flow = new DiscordConfigFlow(() => _client, _oauth);
	}

	[TearDown]
	public void TearDown() => _client.Dispose();

	[Test]
	public async Task The_first_step_collects_the_application_credentials()
	{
		var result = await _flow.StartAsync(new StubContext(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Step));
			Assert.That(result.NextStep!.StepId, Is.EqualTo("credentials"));
			Assert.That(result.NextStep.Fields.Select(f => f.Name), Is.EqualTo(_credentialFields));
		});
	}

	[Test]
	public async Task The_first_step_tells_the_user_which_redirect_uri_to_register()
	{
		var result = await _flow.StartAsync(new StubContext(), CancellationToken.None);

		var copyValue = result.NextStep!.Instructions.SelectMany(i => i.Values).Single();
		Assert.Multiple(() =>
		{
			Assert.That(TestLocalization.Resolve(copyValue.Label), Is.EqualTo("Redirect URI"));
			Assert.That(copyValue.Value, Is.EqualTo(DiscordOAuthClient.RedirectUri));
		});
	}

	[Test]
	public async Task Missing_credentials_are_reported_per_field()
	{
		var result = await Submit("credentials", new Dictionary<string, object?>(StringComparer.Ordinal));

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.FieldErrors!.Keys, Is.EquivalentTo(_credentialFields));
		});
	}

	[Test]
	public async Task A_client_id_that_is_not_a_snowflake_is_rejected()
	{
		var result = await Submit("credentials", Credentials(clientId: "my-app"));

		Assert.That(TestLocalization.Resolve(result.FieldErrors!["clientId"]), Does.Contain("only digits"));
	}

	[Test]
	public async Task Valid_credentials_advance_to_the_authorization_step()
	{
		var result = await Submit("credentials", Credentials());

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Step));
			Assert.That(result.NextStep!.StepId, Is.EqualTo("authorize"));
		});
	}

	[Test]
	public async Task Authorizing_persists_the_tokens_as_secrets()
	{
		await Submit("credentials", Credentials());

		var result = await Submit("authorize", new Dictionary<string, object?>(StringComparer.Ordinal));

		Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
		Assert.Multiple(() =>
		{
			Assert.That(result.EntryTitle, Is.EqualTo("Discord (Tester)"));
			Assert.That(result.Values!["accessToken"].IsSecret, Is.True);
			Assert.That(result.Values["refreshToken"].IsSecret, Is.True);
			Assert.That(result.Values["accessToken"].Value, Is.EqualTo("fresh-access"));
			Assert.That(result.Values.ContainsKey("expiresAt"), Is.True);
			Assert.That(_oauth.ExchangeCount, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task The_authorization_request_asks_for_the_full_scope_set_first()
	{
		await Submit("credentials", Credentials());

		await Submit("authorize", new Dictionary<string, object?>(StringComparer.Ordinal));

		var authorize = _client.CallsTo("AUTHORIZE").Single();
		Assert.Multiple(() =>
		{
			Assert.That(authorize.ArgsJson, Does.Contain("\"client_id\":\"123456\""));
			Assert.That(authorize.ArgsJson, Does.Contain("rpc.voice.write"));
			Assert.That(authorize.ArgsJson, Does.Contain("rpc.notifications.read"));
		});
	}

	[Test]
	public async Task A_refused_scope_is_retried_without_notification_access()
	{
		_client.Fails("AUTHORIZE", new DiscordRpcException(4007, "Invalid scope"));
		await Submit("credentials", Credentials());

		var result = await Submit("authorize", new Dictionary<string, object?>(StringComparer.Ordinal));

		Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
		var second = _client.CallsTo("AUTHORIZE").Last();
		Assert.Multiple(() =>
		{
			Assert.That(_client.CallsTo("AUTHORIZE").Count(), Is.EqualTo(2));
			Assert.That(second.ArgsJson, Does.Not.Contain("rpc.notifications.read"));
			Assert.That(second.ArgsJson, Does.Contain("rpc.voice.write"));
		});
	}

	[Test]
	public async Task A_missing_discord_client_is_reported_as_such()
	{
		_client.ConnectException = new DiscordIpcUnavailableException("nothing listening");
		await Submit("credentials", Credentials());

		var result = await Submit("authorize", new Dictionary<string, object?>(StringComparer.Ordinal));

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("No running Discord client"));
		});
	}

	[Test]
	public async Task A_refused_endpoint_explains_the_privilege_mismatch()
	{
		_client.ConnectException = new DiscordIpcUnavailableException("refused", accessDenied: true);
		await Submit("credentials", Credentials());

		var result = await Submit("authorize", new Dictionary<string, object?>(StringComparer.Ordinal));

		Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("same privileges"));
	}

	[Test]
	public async Task A_user_who_declines_the_prompt_gets_a_readable_error()
	{
		_client.Fails("AUTHORIZE", new DiscordRpcException(4001, "User declined"));
		await Submit("credentials", Credentials());

		var result = await Submit("authorize", new Dictionary<string, object?>(StringComparer.Ordinal));

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("User declined"));
		});
	}

	[Test]
	public async Task A_rejected_client_secret_is_reported_from_the_token_endpoint()
	{
		_oauth.Exception = new DiscordOAuthException("Discord rejected the Client ID or Client Secret.");
		await Submit("credentials", Credentials());

		var result = await Submit("authorize", new Dictionary<string, object?>(StringComparer.Ordinal));

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("Client Secret"));
		});
	}

	[Test]
	public async Task A_failed_account_lookup_still_completes_with_a_generic_title()
	{
		_client.Fails("AUTHENTICATE", new DiscordRpcException(4000, "nope"));
		await Submit("credentials", Credentials());

		var result = await Submit("authorize", new Dictionary<string, object?>(StringComparer.Ordinal));

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(result.EntryTitle, Is.EqualTo("Discord"));
		});
	}

	[Test]
	public async Task An_unknown_step_returns_to_the_credentials_step()
	{
		var result = await Submit("nonsense", new Dictionary<string, object?>(StringComparer.Ordinal));

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.NextStep!.StepId, Is.EqualTo("credentials"));
		});
	}

	private static Dictionary<string, object?> Credentials(string clientId = "123456", string secret = "shh")
		=> new(StringComparer.Ordinal)
		{
			["clientId"] = clientId,
			["clientSecret"] = secret
		};

	private Task<ConfigFlowResult> Submit(string stepId, IReadOnlyDictionary<string, object?> input)
		=> _flow.SubmitAsync(stepId, input, new StubContext(), CancellationToken.None);

	private sealed class StubContext : IConfigFlowContext
	{
		public IOAuthSession OAuth { get; } = new StubOAuthSession();
	}

	private sealed class StubOAuthSession : IOAuthSession
	{
		public string RedirectUri => $"http://127.0.0.1:{BuildConfig.PublicPort}/api/integrations/oauth/callback";

		public string State => "unused";

		public string? AuthorizationCode => null;
	}
}
