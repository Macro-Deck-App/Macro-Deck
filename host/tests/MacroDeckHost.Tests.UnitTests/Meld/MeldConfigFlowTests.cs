using System.Globalization;
using System.Text.Json;
using MacroDeckHost.Integrations.Meld;
using MacroDeckHost.Integrations.Meld.Protocol;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.ConfigFlow;

namespace MacroDeckHost.Tests.UnitTests.Meld;

[TestFixture]
internal sealed class MeldConfigFlowTests
{
	private static readonly string[] _expectedAdvancedFields = ["host", "port"];

	private FakeConfigFlowContext _context = null!;

	[SetUp]
	public void SetUp()
	{
		_context = new FakeConfigFlowContext();
	}

	[Test]
	public async Task StartAsync_returns_the_connection_step_with_empty_fields_and_two_non_required_advanced_fields()
	{
		var flow = new MeldConfigFlow(() => new FakeQWebChannelClient());

		var result = await flow.StartAsync(_context, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Step));
			Assert.That(result.NextStep!.StepId, Is.EqualTo("connection"));
			Assert.That(result.NextStep.Fields, Is.Empty);
			Assert.That(result.NextStep.AdvancedFields, Has.Count.EqualTo(2));
			Assert.That(result.NextStep.AdvancedFields.Select(f => f.Name), Is.EquivalentTo(_expectedAdvancedFields));
			Assert.That(result.NextStep.AdvancedFields, Has.All.Property("Required").False);
		});
	}

	[Test]
	public async Task Submitting_with_no_input_at_all_defaults_host_and_port_and_completes()
	{
		var client = new FakeQWebChannelClient();
		client.Objects["meld"] = FakeQWebChannelClient.BuildMeldObjectInfo();
		var flow = new MeldConfigFlow(() => client);

		var result = await flow.SubmitAsync("connection",
			new Dictionary<string, object?>(StringComparer.Ordinal),
			_context,
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(result.Values!["host"].Value, Is.EqualTo(MeldEndpoint.DefaultHost));
			Assert.That(result.Values["port"].Value,
				Is.EqualTo(MeldEndpoint.DefaultPort.ToString(CultureInfo.InvariantCulture)));
		});
	}

	[TestCase(0)]
	[TestCase(70000)]
	public async Task An_out_of_range_port_produces_a_field_error_naming_the_default(int port)
	{
		var flow = new MeldConfigFlow(() => new FakeQWebChannelClient());

		var result = await flow.SubmitAsync("connection",
			Input(port: port),
			_context,
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.FieldErrors, Is.Not.Null);
			Assert.That(TestLocalization.Resolve(result.FieldErrors!["port"]), Does.Contain("13376"));
		});
	}

	[TestCase("127.0.0.1")]
	[TestCase("localhost")]
	[TestCase("::1")]
	public async Task A_loopback_host_goes_straight_to_the_connection_test(string host)
	{
		var client = new FakeQWebChannelClient();
		client.Objects["meld"] = FakeQWebChannelClient.BuildMeldObjectInfo();
		var flow = new MeldConfigFlow(() => client);

		var result = await flow.SubmitAsync("connection", Input(host), _context, CancellationToken.None);

		Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
	}

	[TestCase("192.168.1.5")]
	[TestCase("streaming-pc.local")]
	public async Task A_remote_host_returns_the_remote_warning_step(string host)
	{
		var flow = new MeldConfigFlow(() => new FakeQWebChannelClient());

		var result = await flow.SubmitAsync("connection", Input(host), _context, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Step));
			Assert.That(result.NextStep!.StepId, Is.EqualTo("remote-warning"));
		});
	}

	[Test]
	public async Task Submitting_remote_warning_without_confirm_redisplays_it_with_a_field_error()
	{
		var flow = new MeldConfigFlow(() => new FakeQWebChannelClient());
		await flow.SubmitAsync("connection", Input("192.168.1.5"), _context, CancellationToken.None);

		var result = await flow.SubmitAsync("remote-warning",
			new Dictionary<string, object?>(StringComparer.Ordinal),
			_context,
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.NextStep!.StepId, Is.EqualTo("remote-warning"));
			Assert.That(result.FieldErrors, Is.Not.Null.And.ContainKey("confirm"));
		});
	}

	[Test]
	public async Task Submitting_remote_warning_with_confirm_proceeds_to_the_connection_test()
	{
		var client = new FakeQWebChannelClient();
		client.Objects["meld"] = FakeQWebChannelClient.BuildMeldObjectInfo();
		var flow = new MeldConfigFlow(() => client);
		await flow.SubmitAsync("connection", Input("192.168.1.5"), _context, CancellationToken.None);

		var result = await flow.SubmitAsync("remote-warning",
			new Dictionary<string, object?>(StringComparer.Ordinal) { ["confirm"] = true },
			_context,
			CancellationToken.None);

		Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
	}

	[Test]
	public async Task A_failed_test_after_the_remote_warning_stays_on_the_remote_warning_step()
	{
		var client = new FakeQWebChannelClient { ConnectException = new InvalidOperationException("refused") };
		var flow = new MeldConfigFlow(() => client);
		await flow.SubmitAsync("connection", Input("192.168.1.5"), _context, CancellationToken.None);

		var result = await flow.SubmitAsync("remote-warning",
			new Dictionary<string, object?>(StringComparer.Ordinal) { ["confirm"] = true },
			_context,
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.NextStep!.StepId, Is.EqualTo("remote-warning"));
			Assert.That(TestLocalization.Resolve(result.NextStep.Description), Does.Contain("192.168.1.5"));
		});
	}

	[Test]
	public async Task An_unreachable_endpoint_reports_that_it_could_not_be_reached()
	{
		var client = new FakeQWebChannelClient { ConnectException = new InvalidOperationException("refused") };
		var flow = new MeldConfigFlow(() => client);

		var result = await flow.SubmitAsync("connection", Input(), _context, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("running"));
			Assert.That(client.Disposed, Is.True);
		});
	}

	[Test]
	public async Task A_handshake_without_a_meld_object_reports_a_distinct_wrong_server_error()
	{
		var client = new FakeQWebChannelClient();
		client.Objects["something-else"] =
			new QWebChannelObjectInfo("something-else",
				new Dictionary<string, int>(StringComparer.Ordinal),
				new Dictionary<string, int>(StringComparer.Ordinal),
				new Dictionary<int, string>(),
				new Dictionary<int, string>(),
				new Dictionary<string, JsonElement>(StringComparer.Ordinal));
		var flow = new MeldConfigFlow(() => client);

		var result = await flow.SubmitAsync("connection", Input(), _context, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("not Meld Studio"));
			Assert.That(client.Disposed, Is.True);
		});
	}

	[Test]
	public async Task A_successful_handshake_completes_and_disposes_the_probe_client()
	{
		var client = new FakeQWebChannelClient();
		client.Objects["meld"] = FakeQWebChannelClient.BuildMeldObjectInfo();
		var flow = new MeldConfigFlow(() => client);

		var result = await flow.SubmitAsync("connection", Input(), _context, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(result.EntryTitle, Is.EqualTo("Meld Studio"));
			Assert.That(client.Disposed, Is.True);
		});
	}

	private static Dictionary<string, object?> Input(string? host = null, int? port = null)
		=> new(StringComparer.Ordinal)
		{
			["host"] = host,
			["port"] = port
		};

	private sealed class FakeConfigFlowContext : IConfigFlowContext
	{
		public IOAuthSession OAuth => throw new NotSupportedException();
	}
}
