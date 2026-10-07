using MacroDeck.Sdk.ConfigFlow;
using MacroDeckHost.Integrations.Jellyfin;
using MacroDeckHost.Integrations.Jellyfin.Protocol;

namespace MacroDeckHost.Tests.UnitTests.Jellyfin;

[TestFixture]
internal sealed class JellyfinConfigFlowTests
{
	[Test]
	public async Task A_rejected_api_key_is_reported_on_the_api_key_field()
	{
		var flow = new JellyfinConfigFlow(_ => new FakeJellyfinClient { RejectCredentials = true });

		var result = await flow.SubmitAsync("connection", Input(apiKey: "wrong"), new NewEntry(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.FieldErrors!.Keys, Is.EquivalentTo(new[] { JellyfinConfigKeys.ApiKey }));
		});
	}

	[Test]
	public async Task An_unreachable_server_does_not_complete()
	{
		var flow = new JellyfinConfigFlow(_ => new FakeJellyfinClient { Unreachable = true });

		var result = await flow.SubmitAsync("connection", Input(apiKey: "key"), new NewEntry(), CancellationToken.None);

		Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
	}

	[Test]
	public async Task Signing_in_stores_the_issued_token_and_never_the_password()
	{
		var flow = new JellyfinConfigFlow(_ => new FakeJellyfinClient { IssuedToken = "token-1" });
		var input = Input(method: JellyfinConfigKeys.AuthLogin, username: "alex", password: "hunter2");

		var result = await flow.SubmitAsync("connection", input, new NewEntry(), CancellationToken.None);

		Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
		var values = result.Values!;
		Assert.Multiple(() =>
		{
			Assert.That(values[JellyfinConfigKeys.AccessToken].IsSecret, Is.True);
			Assert.That(values[JellyfinConfigKeys.AccessToken].Value, Is.EqualTo("token-1"));
			Assert.That(values[JellyfinConfigKeys.Password].IsSecret, Is.False);
			Assert.That(values[JellyfinConfigKeys.Password].Value, Is.Empty);
			Assert.That(values[JellyfinConfigKeys.DeviceId].Value, Is.Not.Empty);
			Assert.That(values.Values.Select(value => value.Value), Has.None.EqualTo("hunter2"));
		});
	}

	[Test]
	public async Task Editing_a_sign_in_without_a_new_password_keeps_the_stored_token_and_device()
	{
		var client = new FakeJellyfinClient();
		var flow = new JellyfinConfigFlow(_ => client);
		var input = Input(method: JellyfinConfigKeys.AuthLogin, username: "alex", password: string.Empty);
		input[JellyfinConfigKeys.AccessToken] = "stored-token";
		input[JellyfinConfigKeys.DeviceId] = "stored-device";

		var result = await flow.SubmitAsync("connection", input, new ExistingEntry(), CancellationToken.None);

		Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
		Assert.Multiple(() =>
		{
			Assert.That(result.Values!.ContainsKey(JellyfinConfigKeys.AccessToken), Is.False);
			Assert.That(result.Values[JellyfinConfigKeys.DeviceId].Value, Is.EqualTo("stored-device"));
			Assert.That(client.Commands, Has.None.StartsWith("authenticate"));
		});
	}

	[TestCase("192.168.1.10:8096", "http://192.168.1.10:8096/")]
	[TestCase("https://media.example.com/jellyfin", "https://media.example.com/jellyfin")]
	public void Server_addresses_default_to_http_and_keep_a_base_path(string text, string expected)
	{
		Assert.That(JellyfinConfigFlow.TryParseUrl(text, out var uri), Is.True);
		Assert.That(uri!.ToString(), Is.EqualTo(expected));
	}

	private static Dictionary<string, object?> Input(
		string method = JellyfinConfigKeys.AuthApiKey,
		string? apiKey = null,
		string? username = null,
		string? password = null)
		=> new()
		{
			[JellyfinConfigKeys.ConfigurationName] = "Home",
			[JellyfinConfigKeys.Url] = "http://jellyfin.local:8096",
			[JellyfinConfigKeys.AuthMethod] = method,
			[JellyfinConfigKeys.ApiKey] = apiKey,
			[JellyfinConfigKeys.Username] = username,
			[JellyfinConfigKeys.Password] = password
		};

	private sealed class NewEntry : IConfigFlowContext
	{
		public IOAuthSession OAuth => throw new NotSupportedException();
	}

	private sealed class ExistingEntry : IConfigFlowEntryContext
	{
		public IOAuthSession OAuth => throw new NotSupportedException();

		public string? EntryTitle => "Home";
	}
}
