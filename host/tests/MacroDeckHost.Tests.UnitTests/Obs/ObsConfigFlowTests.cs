using MacroDeckHost.Integrations.Obs;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.ConfigFlow;

namespace MacroDeckHost.Tests.UnitTests.Obs;

[TestFixture]
internal sealed class ObsConfigFlowTests
{
	private static readonly IConfigFlowContext _context = new FakeConfigFlowContext();

	private static Dictionary<string, object?> ConnectionInput(
		string? configurationName = "Streaming PC",
		string? host = "127.0.0.1",
		object? port = null,
		string? password = "secret")
		=> new()
		{
			[ObsConfigKeys.ConfigurationName] = configurationName,
			[ObsConfigKeys.Host] = host,
			[ObsConfigKeys.Port] = port ?? 4455,
			[ObsConfigKeys.Password] = password
		};

	[Test]
	public async Task StartNewConfiguration_ShowsNameAndConnectionFieldsInOneStep()
	{
		var flow = new ObsConfigFlow(() => new FakeObsClient());

		var result = await flow.StartAsync(_context, CancellationToken.None);

		Assert.That(result.NextStep!.Fields.Select(field => field.Name),
			Is.EqualTo(new[]
			{
				ObsConfigKeys.ConfigurationName,
				ObsConfigKeys.Host,
				ObsConfigKeys.Port,
				ObsConfigKeys.Password
			}));
	}

	[Test]
	public async Task StartExistingConfiguration_ShowsOnlyConnectionFields()
	{
		var flow = new ObsConfigFlow(() => new FakeObsClient());

		var result = await flow.StartAsync(new FakeConfigFlowContext("Studio OBS"), CancellationToken.None);

		Assert.That(result.NextStep!.Fields.Select(field => field.Name),
			Is.EqualTo(new[]
			{
				ObsConfigKeys.Host,
				ObsConfigKeys.Port,
				ObsConfigKeys.Password
			}));
	}

	[Test]
	public async Task SubmitConnection_MissingConfigurationName_ReturnsFieldError()
	{
		var flow = new ObsConfigFlow(() => new FakeObsClient { ConnectRaisesConnected = true });

		var result = await flow.SubmitAsync("connection",
			ConnectionInput(configurationName: ""),
			_context,
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.FieldErrors!.Keys, Does.Contain(ObsConfigKeys.ConfigurationName));
		});
	}

	[Test]
	public async Task SubmitConnection_MissingHost_ReturnsFieldError()
	{
		var flow = new ObsConfigFlow(() => new FakeObsClient { ConnectRaisesConnected = true });

		var result = await flow.SubmitAsync("connection", ConnectionInput(host: ""), _context, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.FieldErrors!.Keys, Does.Contain(ObsConfigKeys.Host));
		});
	}

	[Test]
	public async Task SubmitConnection_InvalidPort_ReturnsFieldError()
	{
		var flow = new ObsConfigFlow(() => new FakeObsClient { ConnectRaisesConnected = true });

		var result = await flow.SubmitAsync("connection",
			ConnectionInput(port: 70000),
			_context,
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.FieldErrors!.Keys, Does.Contain(ObsConfigKeys.Port));
		});
	}

	[Test]
	public async Task SubmitConnection_ConnectionFails_ReturnsError()
	{
		var fake = new FakeObsClient { ConnectRaisesDisconnected = true, DisconnectReason = "Authentication failed." };
		var flow = new ObsConfigFlow(() => fake);

		var result = await flow.SubmitAsync("connection", ConnectionInput(), _context, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("Authentication failed."));
		});
	}

	[Test]
	public async Task SubmitConnection_Success_CompletesWithSecretPassword()
	{
		var fake = new FakeObsClient { ConnectRaisesConnected = true };
		var flow = new ObsConfigFlow(() => fake);

		var result = await flow.SubmitAsync("connection", ConnectionInput(), _context, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(result.EntryTitle, Is.EqualTo("Streaming PC"));
			Assert.That(result.Values![ObsConfigKeys.Host].Value, Is.EqualTo("127.0.0.1"));
			Assert.That(result.Values[ObsConfigKeys.Port].Value, Is.EqualTo("4455"));
			Assert.That(result.Values[ObsConfigKeys.Password].IsSecret, Is.True);
			Assert.That(result.Values[ObsConfigKeys.Password].Value, Is.EqualTo("secret"));
			Assert.That(fake.LastUrl, Is.EqualTo("ws://127.0.0.1:4455"));
		});
	}

	[Test]
	public async Task SubmitConnection_NoPassword_OmitsPasswordValue()
	{
		var fake = new FakeObsClient { ConnectRaisesConnected = true };
		var flow = new ObsConfigFlow(() => fake);

		var result = await flow.SubmitAsync("connection",
			ConnectionInput(password: ""),
			_context,
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(result.Values!.ContainsKey(ObsConfigKeys.Password), Is.False);
		});
	}

	private sealed class FakeConfigFlowContext(string? entryTitle = null)
		: IConfigFlowEntryContext
	{
		public IOAuthSession OAuth { get; } = new FakeOAuthSession();
		public string? EntryTitle { get; } = entryTitle;
	}

	private sealed class FakeOAuthSession : IOAuthSession
	{
		public string RedirectUri => "http://localhost/callback";
		public string State => "state";
		public string? AuthorizationCode => null;
	}
}
