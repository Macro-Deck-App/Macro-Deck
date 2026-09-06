using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Hosting.Validation;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Versioning;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class ValidationTests
{
	private static DeclaredCapability Capability(string localId, string kind = CapabilityKinds.Actions)
		=> new()
		{
			Kind = kind,
			LocalId = localId,
			VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
		};

	[Test]
	public void A_well_formed_catalogue_has_no_conflicts()
	{
		var conflicts = PluginCapabilityValidator.Validate("com.example.test",
			[Capability("play"), Capability("pause")]);

		Assert.That(conflicts, Is.Empty);
	}

	[Test]
	public void An_illegal_local_id_is_refused()
	{
		var conflicts = PluginCapabilityValidator.Validate("com.example.test", [Capability("Not A Valid Id")]);

		Assert.That(conflicts, Has.Count.EqualTo(1));
	}

	[Test]
	public void A_repeated_local_id_within_a_kind_is_refused()
	{
		var conflicts = PluginCapabilityValidator.Validate("com.example.test",
			[Capability("play"), Capability("play")]);

		Assert.That(conflicts.Single().ToString(), Does.Contain("more than once"));
	}

	[Test]
	public void The_same_local_id_under_two_kinds_is_two_capabilities_not_a_collision()
	{
		var conflicts = PluginCapabilityValidator.Validate("com.example.test",
			[Capability("current", CapabilityKinds.Actions), Capability("current", CapabilityKinds.Variables)]);

		Assert.That(conflicts, Is.Empty);
	}

	[Test]
	public void An_unknown_kind_is_refused_and_lists_the_known_ones()
	{
		var conflicts = PluginCapabilityValidator.Validate("com.example.test", [Capability("one", "telepathy")]);

		Assert.That(conflicts.Single().ToString(), Does.Contain(CapabilityKinds.Actions));
	}

	[Test]
	public void Declaring_more_than_the_protocol_allows_is_refused()
	{
		var capabilities = Enumerable.Range(0, ProtocolLimits.MaxDeclaredCapabilities + 1)
			.Select(index => Capability($"action-{index}"))
			.ToList();

		var conflicts = PluginCapabilityValidator.Validate("com.example.test", capabilities);

		Assert.That(conflicts,
			Has.One.Matches<Sdk.Identity.CapabilityIdConflict>(conflict
				=> conflict.Reason.Contains("at most", StringComparison.Ordinal)));
	}

	[Test]
	public void Two_integrations_declaring_the_same_action_id_fail_the_build()
	{
		// New constraint out of process: the owner is the plugin, so an action id has to be unique
		// across every integration in the process rather than only within one.
		var exception = Assert.Throws<PluginConfigurationException>(() => MacroDeckPlugin.CreatePlugin()
			.RegisterIntegration<FirstPlay>()
			.RegisterIntegration<SecondPlay>()
			.Build());

		Assert.That(exception!.Problems, Has.One.Contains("play"));
	}

	[Test]
	public void An_integration_declaring_an_illegal_action_id_fails_the_build()
	{
		var exception = Assert.Throws<PluginConfigurationException>(() => MacroDeckPlugin.CreatePlugin()
			.RegisterIntegration<IllegalActionId>()
			.Build());

		Assert.That(exception!.Problems, Is.Not.Empty);
	}

	private sealed class FirstPlay() : TestIntegration(new TestAction("play"));

	private sealed class SecondPlay() : TestIntegration(new TestAction("play"));

	private sealed class IllegalActionId() : TestIntegration(new TestAction("Not Valid"));
}
