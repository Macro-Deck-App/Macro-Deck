using MacroDeckHost.Integrations.Streamerbot;
using MacroDeckHost.Integrations.Streamerbot.Protocol;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.ConfigFlow;

namespace MacroDeckHost.Tests.UnitTests.Streamerbot;

[TestFixture]
internal sealed class StreamerbotConfigFlowTests
{
	private static readonly StreamerbotInstanceInfo _info = new("Streamer.bot", "0.2.5", "instance-1", "windows");

	private static readonly string[] _fieldNames = ["host", "port", "endpoint", "password"];

	private FakeStreamerbotClient _client = null!;
	private StreamerbotConfigFlow _flow = null!;

	[SetUp]
	public void SetUp()
	{
		_client = new FakeStreamerbotClient
		{
			Hello = new StreamerbotHello(_info, null),
			Responses =
			{
				["GetInfo"] =
					"""{ "status": "ok", "info": { "instanceId": "instance-1", "name": "Streamer.bot", "version": "0.2.5" } }"""
			}
		};

		_flow = new StreamerbotConfigFlow(() => _client);
	}

	[TearDown]
	public void TearDown()
	{
		_client.Dispose();
	}

	[Test]
	public async Task The_flow_starts_on_the_connection_step()
	{
		var result = await _flow.StartAsync(null!, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Step));
			Assert.That(result.NextStep?.StepId, Is.EqualTo("connection"));
			Assert.That(result.NextStep?.Fields.Select(field => field.Name), Is.EquivalentTo(_fieldNames));
		});
	}

	[Test]
	public async Task An_empty_host_is_a_field_error()
	{
		var result = await Submit(host: " ");

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.FieldErrors, Contains.Key("host"));
			Assert.That(_client.ConnectCount, Is.Zero);
		});
	}

	[Test]
	public async Task An_out_of_range_port_is_a_field_error()
	{
		var result = await Submit(port: 70_000);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.FieldErrors, Contains.Key("port"));
			Assert.That(_client.ConnectCount, Is.Zero);
		});
	}

	[Test]
	public async Task A_successful_test_completes_with_the_collected_values()
	{
		var result = await Submit();

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(result.Values?["host"].Value, Is.EqualTo("127.0.0.1"));
			Assert.That(result.Values?["port"].Value, Is.EqualTo("8080"));
			Assert.That(result.Values?["endpoint"].Value, Is.EqualTo("/"));
			Assert.That(result.Values?.ContainsKey("password"), Is.False);
			Assert.That(result.EntryTitle, Does.Contain("0.2.5"));
		});
	}

	[Test]
	public async Task A_pasted_address_is_normalised_before_it_is_stored()
	{
		var result = await Submit(host: "ws://192.168.0.5:8080/");

		Assert.Multiple(() =>
		{
			Assert.That(result.Values?["host"].Value, Is.EqualTo("192.168.0.5"));
			Assert.That(_client.LastUri?.ToString(), Is.EqualTo("ws://192.168.0.5:8080/"));
		});
	}

	[Test]
	public async Task The_endpoint_is_part_of_the_tested_url()
	{
		await Submit(endpoint: "ws");

		Assert.That(_client.LastUri?.ToString(), Is.EqualTo("ws://127.0.0.1:8080/ws"));
	}

	[Test]
	public async Task A_password_is_stored_encrypted()
	{
		_client.Hello = new StreamerbotHello(_info, new StreamerbotChallenge("c2FsdA==", "Y2hhbGxlbmdl"));

		var result = await Submit(password: "streamer");

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(result.Values?["password"].IsSecret, Is.True);
			Assert.That(result.Values?["password"].Value, Is.EqualTo("streamer"));
		});
	}

	[Test]
	public async Task The_password_is_verified_before_the_entry_is_saved()
	{
		_client.Hello = new StreamerbotHello(_info, new StreamerbotChallenge("c2FsdA==", "Y2hhbGxlbmdl"));
		_client.FailingRequests.Add("Authenticate");

		var result = await Submit(password: "wrong");

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("rejected the password"));
		});
	}

	[Test]
	public async Task A_server_that_enforces_authentication_asks_for_the_password()
	{
		_client.Hello = new StreamerbotHello(_info, new StreamerbotChallenge("c2FsdA==", "Y2hhbGxlbmdl"));
		_client.FailingRequests.Add("GetInfo");

		var result = await Submit();

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("requires a WebSocket password"));
		});
	}

	[Test]
	public async Task An_unreachable_server_is_reported_on_the_step()
	{
		_client.ConnectException = new InvalidOperationException("connection refused");

		var result = await Submit();

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("Could not connect"));
			Assert.That(result.NextStep?.StepId, Is.EqualTo("connection"));
		});
	}

	[Test]
	public async Task A_server_that_does_not_greet_is_still_accepted()
	{
		_client.Hello = null;

		var result = await Submit();

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(_client.RequestNames, Has.Member("GetInfo"));
		});
	}

	[Test]
	public async Task An_unknown_step_is_rejected()
	{
		var result = await _flow.SubmitAsync("nonsense",
			new Dictionary<string, object?>(StringComparer.Ordinal),
			null!,
			CancellationToken.None);

		Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
	}

	private Task<ConfigFlowResult> Submit(
		string host = "127.0.0.1",
		int port = 8080,
		string endpoint = "/",
		string password = "")
		=> _flow.SubmitAsync("connection",
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				["host"] = host,
				["port"] = port,
				["endpoint"] = endpoint,
				["password"] = password
			},
			null!,
			CancellationToken.None);
}
