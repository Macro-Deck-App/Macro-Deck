using MacroDeckHost.Integrations.SinusBot;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.ConfigFlow;

namespace MacroDeckHost.Tests.UnitTests.SinusBot;

[TestFixture]
internal sealed class SinusBotConfigFlowTests
{
	private static readonly IConfigFlowContext _context = new FakeConfigFlowContext();

	private static Dictionary<string, object?> ConnectionInput(string? url = "http://bot:8087",
		string? user = "admin",
		string? password = "secret")
		=> new()
		{
			[SinusBotConfigKeys.ServerUrl] = url,
			[SinusBotConfigKeys.Username] = user,
			[SinusBotConfigKeys.Password] = password
		};

	[Test]
	public async Task SubmitConnection_MissingFields_ReturnsFieldErrors()
	{
		var flow = new SinusBotConfigFlow(_ => new FakeSinusBotClient());

		var result = await flow.SubmitAsync("connection",
			new Dictionary<string, object?>(),
			_context,
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.FieldErrors, Is.Not.Null);
			Assert.That(result.FieldErrors!.Keys, Does.Contain(SinusBotConfigKeys.ServerUrl));
			Assert.That(result.FieldErrors.Keys, Does.Contain(SinusBotConfigKeys.Username));
			Assert.That(result.FieldErrors.Keys, Does.Contain(SinusBotConfigKeys.Password));
		});
	}

	[Test]
	public async Task SubmitConnection_AuthFailure_ReturnsError()
	{
		var fake = new FakeSinusBotClient
			{ AuthException = new SinusBotAuthException("Wrong SinusBot username or password.") };
		var flow = new SinusBotConfigFlow(_ => fake);

		var result = await flow.SubmitAsync("connection", ConnectionInput(), _context, CancellationToken.None);

		Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
		Assert.That(TestLocalization.Resolve(result.ErrorMessage),
			Does.Contain("Wrong SinusBot username or password."));
	}

	[Test]
	public async Task SubmitConnection_NoInstances_ReturnsError()
	{
		var fake = new FakeSinusBotClient { Instances = [] };
		var flow = new SinusBotConfigFlow(_ => fake);

		var result = await flow.SubmitAsync("connection", ConnectionInput(), _context, CancellationToken.None);

		Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
		Assert.That(TestLocalization.Resolve(result.ErrorMessage), Is.Not.Null);
	}

	[Test]
	public async Task SubmitConnection_Success_ReturnsInstanceStep()
	{
		var fake = new FakeSinusBotClient
		{
			Instances =
			[
				new SinusBotInstance { Uuid = "u1", Name = "Bot1" },
				new SinusBotInstance { Uuid = "u2", Name = "Bot2" }
			]
		};
		var flow = new SinusBotConfigFlow(_ => fake);

		var result = await flow.SubmitAsync("connection", ConnectionInput(), _context, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Step));
			Assert.That(result.NextStep!.StepId, Is.EqualTo("instance"));
			var choice = result.NextStep.Fields.Single();
			Assert.That(choice.Options!.Count, Is.EqualTo(2));
		});
	}

	[Test]
	public async Task SubmitInstance_CompletesWithSecretPassword()
	{
		var fake = new FakeSinusBotClient
		{
			Instances = [new SinusBotInstance { Uuid = "u1", Name = "Bot1" }]
		};
		var flow = new SinusBotConfigFlow(_ => fake);

		await flow.SubmitAsync("connection", ConnectionInput(), _context, CancellationToken.None);
		var instanceInput = new Dictionary<string, object?> { [SinusBotConfigKeys.InstanceId] = "u1" };
		var result = await flow.SubmitAsync("instance", instanceInput, _context, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(result.EntryTitle, Is.EqualTo("SinusBot (Bot1)"));
			Assert.That(result.Values![SinusBotConfigKeys.Password].IsSecret, Is.True);
			Assert.That(result.Values[SinusBotConfigKeys.Password].Value, Is.EqualTo("secret"));
			Assert.That(result.Values[SinusBotConfigKeys.InstanceId].Value, Is.EqualTo("u1"));
			Assert.That(result.Values[SinusBotConfigKeys.ServerUrl].IsSecret, Is.False);
		});
	}

	[Test]
	public async Task SubmitInstance_WithoutSelection_ReturnsError()
	{
		var fake = new FakeSinusBotClient
		{
			Instances = [new SinusBotInstance { Uuid = "u1", Name = "Bot1" }]
		};
		var flow = new SinusBotConfigFlow(_ => fake);

		await flow.SubmitAsync("connection", ConnectionInput(), _context, CancellationToken.None);
		var result = await flow.SubmitAsync("instance",
			new Dictionary<string, object?>(),
			_context,
			CancellationToken.None);

		Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
	}

	private sealed class FakeConfigFlowContext : IConfigFlowContext
	{
		public IOAuthSession OAuth { get; } = new FakeOAuthSession();
	}

	private sealed class FakeOAuthSession : IOAuthSession
	{
		public string RedirectUri => "http://localhost/callback";
		public string State => "state";
		public string? AuthorizationCode => null;
	}
}
