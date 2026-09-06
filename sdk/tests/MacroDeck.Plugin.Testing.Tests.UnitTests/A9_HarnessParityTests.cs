using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Testing.Tests.UnitTests.Support;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

/// <summary>
/// A9 - <see cref="PluginTestHarness" /> and <see cref="MacroDeckTestHost" /> agree on the same plugin:
/// the same six invocations against each produce the same outcome.
/// </summary>
[TestFixture]
public class A9_HarnessParityTests
{
	private static Action<PluginHostBuilder> Configure(string id) => builder => builder
		.RegisterIntegration(_ => new TestIntegration(id)
			.WithAction(new DelegateAction("succeed", _ => Task.FromResult(ActionResult.Success())))
			.WithAction(new DelegateAction("fail",
				_ => Task.FromResult(ActionResult.Failed(ActionErrorCodes.ProviderError, "provider refused"))))
			.WithVariable(VariableDefinition.Eager("temperature", VariableType.Numeric), () => 21.5));

	[Test]
	public async Task The_harness_and_the_wire_produce_identical_outcomes_for_the_same_plugin()
	{
		const string id = "test.a9";

		await using var harness = PluginTestHarness.Create(Configure(id));

		await using var host = await MacroDeckTestHost.StartAsync();
		var hostBuilder = MacroDeckPlugin.CreatePlugin();
		Configure(id)(hostBuilder);
		await using var plugin = await host.HostAsync(hostBuilder);
		var session = await host.WaitForSessionAsync();

		Assert.That(NamesOf(harness.Declared), Is.EquivalentTo(NamesOf(session.Declared)));

		var variableLocalId = harness.Declared.Single(c => c.Kind == CapabilityKinds.Variables).LocalId;

		await AssertSameOutcome(() => harness.Actions.DescribeAsync(),
			() => session.Actions.DescribeAsync());

		await AssertSameOutcome(() => harness.Actions.ExecuteAsync("succeed"),
			() => session.Actions.ExecuteAsync("succeed"));

		await AssertSameOutcome(() => harness.Actions.ExecuteAsync("fail"),
			() => session.Actions.ExecuteAsync("fail"));

		await AssertSameOutcome(() => harness.Variables.DescribeAsync(),
			() => session.Variables.DescribeAsync());

		await AssertSameOutcome(() => harness.Variables.GetAsync(variableLocalId),
			() => session.Variables.GetAsync(variableLocalId));

		await AssertSameOutcome(() => harness.Actions.ExecuteAsync("no-such-action"),
			() => session.Actions.ExecuteAsync("no-such-action"));
	}

	private static IReadOnlyCollection<(string Kind, string LocalId)> NamesOf(IEnumerable<DeclaredCapability> declared)
		=> [.. declared.Select(capability => (capability.Kind, capability.LocalId))];

	private static async Task AssertSameOutcome(
		Func<Task<CapabilityInvocationOutcome>> viaHarness,
		Func<Task<CapabilityInvocationOutcome>> viaHost)
	{
		var harnessOutcome = await viaHarness();
		var hostOutcome = await viaHost();

		Assert.Multiple(() =>
		{
			Assert.That(harnessOutcome.Succeeded, Is.EqualTo(hostOutcome.Succeeded));
			Assert.That(harnessOutcome.Error?.Code, Is.EqualTo(hostOutcome.Error?.Code));
			Assert.That(JsonEquals(harnessOutcome.Data, hostOutcome.Data),
				Is.True,
				$"harness data '{harnessOutcome.Data}' did not match host data '{hostOutcome.Data}' as normalized JSON");
		});
	}

	/// <summary>Compares two <see cref="JsonElement" /> values by JSON structure and value - not
	/// property order, and not as .NET objects, which would let a harness that skipped protocol
	/// serialization pass regardless of what it actually produced.</summary>
	private static bool JsonEquals(JsonElement? a, JsonElement? b)
	{
		if (a is null || b is null)
		{
			return a is null && b is null;
		}

		return JsonNode.DeepEquals(JsonNode.Parse(a.Value.GetRawText()), JsonNode.Parse(b.Value.GetRawText()));
	}
}
