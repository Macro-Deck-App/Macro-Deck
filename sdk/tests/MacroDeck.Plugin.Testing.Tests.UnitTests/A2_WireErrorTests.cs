using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Testing.Tests.UnitTests.Support;
using MacroDeck.Sdk.Actions;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

/// <summary>
/// A2 - the error surfaced is byte-for-byte the error the plugin sent, verified against the wire
/// itself rather than trusted from the outcome alone.
/// </summary>
[TestFixture]
public class A2_WireErrorTests
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
				new TestIntegration("test.a2")
					.WithAction(new DelegateAction("known-action", _ => Task.FromResult(ActionResult.Success()))));

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
	public async Task An_undeclared_local_id_returns_the_plugins_own_wire_error()
	{
		var outcome = await _session.InvokeAsync(CapabilityKinds.Actions,
			"no-such-action",
			CapabilityOperations.Actions.Execute);

		AssertMatchesWireReply(outcome);
		Assert.That(outcome.Succeeded, Is.False);
		Assert.That(outcome.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
	}

	[Test]
	public async Task An_operation_not_in_the_kinds_vocabulary_returns_the_plugins_own_wire_error()
	{
		var outcome = await _session.InvokeAsync(CapabilityKinds.Actions, "known-action", "not-a-real-operation");

		AssertMatchesWireReply(outcome);
		Assert.That(outcome.Succeeded, Is.False);
		Assert.That(outcome.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnsupported));
	}

	[Test]
	public async Task An_unknown_capability_kind_returns_CAPABILITY_UNSUPPORTED()
	{
		var outcome = await _session.InvokeAsync("not-a-real-kind", "whatever", "describe");

		AssertMatchesWireReply(outcome);
		Assert.That(outcome.Succeeded, Is.False);
		Assert.That(outcome.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnsupported));
	}

	/// <summary>
	/// Finds the reply envelope in <see cref="MacroDeckTestHost.Messages" /> by correlation id and
	/// asserts the outcome's error is exactly what that envelope carried - not a value this package
	/// could have synthesised locally without ever asking the plugin.
	/// </summary>
	private void AssertMatchesWireReply(CapabilityInvocationOutcome outcome)
	{
		var reply = _host.Messages.All.Single(message
			=> message.Direction == ProtocolMessageDirection.FromPlugin &&
			string.Equals(message.Envelope.CorrelationId, outcome.CorrelationId, StringComparison.Ordinal));

		Assert.Multiple(() =>
		{
			Assert.That(reply.Envelope.Error, Is.Not.Null);
			Assert.That(outcome.Error!.Code, Is.EqualTo(reply.Envelope.Error!.Code));
			Assert.That(outcome.Error!.Message, Is.EqualTo(reply.Envelope.Error!.Message));
			Assert.That(outcome.Error!.Retryable, Is.EqualTo(reply.Envelope.Error!.Retryable));
			Assert.That(outcome.Error!.Details, Is.EqualTo(reply.Envelope.Error!.Details));
		});
	}
}
