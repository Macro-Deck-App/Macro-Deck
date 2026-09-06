using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Testing.Tests.UnitTests.Support;
using MacroDeck.Sdk.Actions;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

/// <summary>
/// A8 - <see cref="MacroDeckTestHost.Messages" /> is complete, ordered and correlated;
/// <see cref="MacroDeckTestHost.SendRawAsync(string)" /> bypasses envelope construction entirely and
/// never breaks the connection.
/// </summary>
// Deliberately not covered here: "capability.declare precedes the first invoke". MacroDeck.Plugin.Hosting
// never sends a standalone capability.declare at handshake - capabilities travel on the session request
// itself - so no .NET subject can satisfy or violate that ordering; there is nothing to assert.
[TestFixture]
public class A8_MessageLogTests
{
	private MacroDeckTestHost _host = null!;
	private InProcessPlugin _plugin = null!;
	private PluginSessionView _session = null!;

	[SetUp]
	public async Task SetUp()
	{
		_host = await MacroDeckTestHost.StartAsync();

		var builder = MacroDeckPlugin.CreatePlugin()
			.RegisterIntegration(_ =>
				new TestIntegration("test.a8").WithAction(new DelegateAction("quick",
					_ => Task.FromResult(ActionResult.Success()))));

		_plugin = await _host.HostAsync(builder);
		_session = await _host.WaitForSessionAsync();
	}

	[TearDown]
	public async Task TearDown()
	{
		await _plugin.DisposeAsync();
		await _host.DisposeAsync();
	}

	[Test]
	public void Every_envelope_is_recorded_once_with_a_direction_and_correlated_correctly()
	{
		var all = _host.Messages.All;

		Assert.That(all, Is.Not.Empty);

		var ids = all.Select(message => message.Envelope.Id).ToList();
		Assert.That(ids, Is.Unique, "an envelope was recorded more than once");

		var helloIndex = all.ToList().FindIndex(message => message.Envelope.Type == MessageTypes.SessionHello);
		var welcomeIndex = all.ToList().FindIndex(message => message.Envelope.Type == MessageTypes.SessionWelcome);

		Assert.Multiple(() =>
		{
			Assert.That(helloIndex, Is.GreaterThanOrEqualTo(0));
			Assert.That(welcomeIndex, Is.GreaterThan(helloIndex), "session.hello must precede session.welcome");
			Assert.That(all[helloIndex].Direction, Is.EqualTo(ProtocolMessageDirection.FromPlugin));
			Assert.That(all[welcomeIndex].Direction, Is.EqualTo(ProtocolMessageDirection.ToPlugin));
		});
	}

	[Test]
	public async Task A_results_correlation_id_equals_the_invokes_id()
	{
		var outcome = await _session.Actions.ExecuteAsync("quick");

		var invoke = _host.Messages.All.Single(message
			=> message.Direction == ProtocolMessageDirection.ToPlugin &&
			message.Envelope.Type == MessageTypes.CapabilityInvoke &&
			message.Envelope.Id == outcome.CorrelationId);

		var result = _host.Messages.All.Single(message
			=> message.Direction == ProtocolMessageDirection.FromPlugin &&
			message.Envelope.Type == MessageTypes.CapabilityResult &&
			message.Envelope.CorrelationId == outcome.CorrelationId);

		Assert.That(result.Envelope.CorrelationId, Is.EqualTo(invoke.Envelope.Id));
	}

	[Test]
	public async Task SendRawAsync_with_invalid_json_reports_MALFORMED_ENVELOPE_and_keeps_the_socket_open()
	{
		await _host.SendRawAsync("{ this is not valid json");

		var error = await _host.NextAsync(MessageTypes.ProtocolError, TimeSpan.FromSeconds(5));
		Assert.That(error.Error!.Code, Is.EqualTo(ProtocolErrorCodes.MalformedEnvelope));

		// The socket must still work afterwards.
		var outcome = await _session.Actions.ExecuteAsync("quick");
		Assert.That(outcome.Succeeded, Is.True);
	}

	[Test]
	public async Task SendRawAsync_with_an_unknown_message_type_reports_UNKNOWN_MESSAGE_TYPE_with_the_raw_envelopes_id()
	{
		const string rawId = "raw-envelope-id-a8";
		var raw = $"{{\"type\":\"not.a.real.type\",\"id\":\"{rawId}\"}}";
		await _host.SendRawAsync(raw);

		var error = await _host.NextAsync(MessageTypes.ProtocolError, TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(error.Error!.Code, Is.EqualTo(ProtocolErrorCodes.UnknownMessageType));
			Assert.That(error.CorrelationId, Is.EqualTo(rawId));
		});

		var outcome = await _session.Actions.ExecuteAsync("quick");
		Assert.That(outcome.Succeeded, Is.True);
	}
}
