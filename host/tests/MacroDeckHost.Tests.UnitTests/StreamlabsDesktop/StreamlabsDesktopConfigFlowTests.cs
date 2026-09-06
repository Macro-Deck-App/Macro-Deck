using System.Text;
using MacroDeckHost.Integrations.StreamlabsDesktop;
using MacroDeckHost.Integrations.StreamlabsDesktop.Protocol;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.ConfigFlow;

namespace MacroDeckHost.Tests.UnitTests.StreamlabsDesktop;

[TestFixture]
public class StreamlabsDesktopConfigFlowTests
{
	private readonly List<FakeStreamlabsClient> _clients = [];

	[TearDown]
	public void TearDown()
	{
		foreach (var client in _clients)
		{
			client.Dispose();
		}

		_clients.Clear();
	}

	[Test]
	public async Task StartAsync_ShowsTheTokenFieldWithTheAddressBehindAdvanced()
	{
		var flow = new StreamlabsDesktopConfigFlow();

		var result = await flow.StartAsync(null!, CancellationToken.None);
		var step = result.NextStep!;

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Step));
			Assert.That(step.Fields.Select(field => field.Name),
				Is.EqualTo(new[] { StreamlabsDesktopConfigKeys.Token }));
			Assert.That(step.AdvancedFields.Select(field => field.Name),
				Is.EqualTo(new[] { StreamlabsDesktopConfigKeys.Host, StreamlabsDesktopConfigKeys.Port }));
			Assert.That(step.AdvancedFields.Select(field => field.Required),
				Is.All.False,
				"the SDK forbids a required advanced field");
			Assert.That(step.Instructions, Is.Not.Empty);
			Assert.That(step.Links, Is.Not.Empty);
		});
	}

	[Test]
	public async Task AnEmptyToken_IsAFieldErrorAndNothingIsDialled()
	{
		var client = Track(StreamlabsJson.Seeded());
		var flow = new StreamlabsDesktopConfigFlow(() => client);

		var result = await Submit(flow,
			new Dictionary<string, object?>
			{
				[StreamlabsDesktopConfigKeys.Token] = "   "
			});

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.FieldErrors?.ContainsKey(StreamlabsDesktopConfigKeys.Token), Is.True);
			Assert.That(client.Calls, Is.Empty);
		});
	}

	[TestCase(0)]
	[TestCase(70000)]
	public async Task AnInvalidPort_IsAFieldError(int port)
	{
		var client = Track(StreamlabsJson.Seeded());
		var flow = new StreamlabsDesktopConfigFlow(() => client);

		var result = await Submit(flow,
			new Dictionary<string, object?>
			{
				[StreamlabsDesktopConfigKeys.Token] = "abc",
				[StreamlabsDesktopConfigKeys.Port] = port
			});

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.FieldErrors?.ContainsKey(StreamlabsDesktopConfigKeys.Port), Is.True);
			Assert.That(client.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task ASuccessfulTest_CompletesWithTheTokenAsASecret()
	{
		var client = Track(StreamlabsJson.Seeded());
		var flow = new StreamlabsDesktopConfigFlow(() => client);

		var result = await Submit(flow,
			new Dictionary<string, object?>
			{
				[StreamlabsDesktopConfigKeys.Token] = "abc123"
			});

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(result.EntryTitle, Is.EqualTo("Streamlabs Desktop"));
			Assert.That(result.Values?[StreamlabsDesktopConfigKeys.Token].IsSecret, Is.True);
			Assert.That(result.Values?[StreamlabsDesktopConfigKeys.Token].Value, Is.EqualTo("abc123"));
			Assert.That(result.Values?[StreamlabsDesktopConfigKeys.Host].Value,
				Is.EqualTo(StreamlabsDesktopEndpoint.DefaultHost));
			Assert.That(result.Values?[StreamlabsDesktopConfigKeys.Port].Value, Is.EqualTo("59650"));
			Assert.That(client.Calls,
				Does.Contain($"{StreamlabsServices.Scenes}.{StreamlabsServices.GetScenes}"),
				"the session is proven usable, not merely open");
			Assert.That(client.DisconnectCalled, Is.True);
			Assert.That(client.Disposed, Is.True);
		});
	}

	[Test]
	public async Task ARemoteHost_AppearsInTheEntryTitle()
	{
		var client = Track(StreamlabsJson.Seeded());
		var flow = new StreamlabsDesktopConfigFlow(() => client);

		var result = await Submit(flow,
			new Dictionary<string, object?>
			{
				[StreamlabsDesktopConfigKeys.Token] = "abc123",
				[StreamlabsDesktopConfigKeys.Host] = "192.168.1.20"
			});

		Assert.Multiple(() =>
		{
			Assert.That(result.EntryTitle, Is.EqualTo("Streamlabs Desktop (192.168.1.20)"));
			Assert.That(result.Values?[StreamlabsDesktopConfigKeys.Host].Value, Is.EqualTo("192.168.1.20"));
		});
	}

	[Test]
	public async Task APastedQrPayload_YieldsTheTokenAndItsPort()
	{
		var client = Track(StreamlabsJson.Seeded());
		var flow = new StreamlabsDesktopConfigFlow(() => client);

		var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"token":"from-qr","port":51234}"""));

		var result = await Submit(flow,
			new Dictionary<string, object?>
			{
				[StreamlabsDesktopConfigKeys.Token] = payload
			});

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(result.Values?[StreamlabsDesktopConfigKeys.Token].Value, Is.EqualTo("from-qr"));
			Assert.That(result.Values?[StreamlabsDesktopConfigKeys.Port].Value, Is.EqualTo("51234"));
		});
	}

	[Test]
	public async Task AnExplicitPort_WinsOverTheOneInThePayload()
	{
		var client = Track(StreamlabsJson.Seeded());
		var flow = new StreamlabsDesktopConfigFlow(() => client);

		var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"token":"from-qr","port":51234}"""));

		var result = await Submit(flow,
			new Dictionary<string, object?>
			{
				[StreamlabsDesktopConfigKeys.Token] = payload,
				[StreamlabsDesktopConfigKeys.Port] = 60000
			});

		Assert.That(result.Values?[StreamlabsDesktopConfigKeys.Port].Value, Is.EqualTo("60000"));
	}

	[Test]
	public async Task ARejectedToken_GetsTheTokenSpecificMessage()
	{
		var client = Track(new FakeStreamlabsClient
		{
			ConnectFailure = new StreamlabsAuthenticationException("nope")
		});

		var flow = new StreamlabsDesktopConfigFlow(() => client);
		var result = await Submit(flow,
			new Dictionary<string, object?>
			{
				[StreamlabsDesktopConfigKeys.Token] = "stale"
			});

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("rejected that API token"));
			Assert.That(client.Disposed, Is.True);
		});
	}

	[Test]
	public async Task AnUnreachableApp_GetsTheReachabilityMessage()
	{
		var client = Track(new FakeStreamlabsClient
		{
			ConnectFailure = new StreamlabsRpcException("connection refused")
		});

		var flow = new StreamlabsDesktopConfigFlow(() => client);
		var result = await Submit(flow,
			new Dictionary<string, object?>
			{
				[StreamlabsDesktopConfigKeys.Token] = "abc"
			});

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage),
				Does.Contain("Could not reach Streamlabs Desktop at 127.0.0.1:59650"));
			Assert.That(client.Disposed, Is.True);
		});
	}

	[Test]
	public async Task AnUnknownStep_IsRejected()
	{
		var flow = new StreamlabsDesktopConfigFlow();

		var result = await flow.SubmitAsync("nope",
			new Dictionary<string, object?>(),
			null!,
			CancellationToken.None);

		Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
	}

	private static Task<ConfigFlowResult> Submit(
		StreamlabsDesktopConfigFlow flow,
		IReadOnlyDictionary<string, object?> input)
		=> flow.SubmitAsync("connection", input, null!, CancellationToken.None);

	private FakeStreamlabsClient Track(FakeStreamlabsClient client)
	{
		_clients.Add(client);
		return client;
	}
}
