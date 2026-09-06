using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;

namespace MacroDeckHost.Tests.PluginContractTests;

/// <summary>
/// The old-consumer regression issue #543 requires: a plugin compiled against the previous SDK runs
/// unchanged, without a recompile.
///
/// <para>
/// Every reply here is written as literal wire JSON rather than by constructing
/// <c>ConfigFlowDescribePayload</c> or <c>ActionDescriptorDto</c> and leaving the new members at their
/// defaults. Constructing today's DTO and asserting its default only proves a C# field initializer;
/// what has to hold is that the *bytes* a plugin built before these fields existed still produce the
/// old behaviour. This plugin also declares no <c>ui</c> capability at all, because that is the other
/// half of what "changed nothing" means.
/// </para>
/// </summary>
[TestFixture]
internal sealed class ConfigUiCompatibilityContractTests : Harness.CapabilityContractFixture
{
	private static readonly CapabilityVersionRange _v1 = new() { Minimum = 1, Maximum = 1 };

	[Test]
	public async Task A_plugin_that_predates_config_ui_declares_no_ui_capability_and_offers_no_tree()
	{
		var invocations = new List<string>();
		var configFlow = new LegacyHandler(CapabilityKinds.ConfigFlow, ConfigFlowDescribeJson, invocations);
		var actions = new LegacyHandler(CapabilityKinds.Actions, ActionsDescribeJson, invocations);

		await ConnectAsync([configFlow, actions],
			[
				new DeclaredCapability
				{
					Kind = CapabilityKinds.ConfigFlow, LocalId = ProviderCapabilityId.LocalId, VersionRange = _v1
				},
				new DeclaredCapability
				{
					Kind = CapabilityKinds.Actions, LocalId = "legacy-action", VersionRange = _v1
				}
			],
			[CapabilityKinds.ConfigFlow, CapabilityKinds.Actions]);

		var describeConfigFlow = await InvokeRawAsync(CapabilityKinds.ConfigFlow,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.ConfigFlow.Describe);

		var describeActions = await InvokeRawAsync(CapabilityKinds.Actions,
			"legacy-action",
			CapabilityOperations.Actions.Describe);

		Assert.Multiple(() =>
		{
			// The absent keys read as "no tree", which is exactly the historic behaviour.
			Assert.That(describeConfigFlow!.Value.TryGetProperty("servesConfigUiTree", out _),
				Is.False,
				"the legacy plugin's own bytes must not carry the new key");
			Assert.That(describeConfigFlow.Value
					.Deserialize<MacroDeck.Plugin.Protocol.Capabilities.ConfigFlow.ConfigFlowDescribePayload>(
						PluginProtocolJson.Options)!.ServesConfigUiTree,
				Is.False);

			Assert.That(describeActions!.Value.TryGetProperty("configuresWithUiTree", out _), Is.False);

			// Nothing asked this plugin for a UI session: it never declared the capability, so the host
			// must not have probed it. "Nothing threw" would not prove that.
			Assert.That(invocations.Where(operation => operation.StartsWith("ui/", StringComparison.Ordinal)),
				Is.Empty,
				"the host invoked a ui operation on a plugin that never declared the ui capability");
		});
	}

	private const string ConfigFlowDescribeJson = """{"allowsMultipleConfigurations":true}""";

	private const string ActionsDescribeJson =
		"""{"actions":[{"localId":"legacy-action","name":"Legacy action","description":"Predates config UI trees","parameters":[{"name":"host","type":"String","required":true},{"name":"apiKey","type":"Secret","required":true}],"supportsDynamicOptions":false,"providesState":false}]}""";

	/// <summary>
	/// One capability of a plugin exactly as it was before #543: it answers <c>describe</c> with the
	/// payload shape that shipped then, byte for byte, and knows nothing about a UI tree.
	/// </summary>
	private sealed class LegacyHandler(string kind, string describeJson, List<string> invocations) : ICapabilityHandler
	{
		public string Kind => kind;

		public IReadOnlyList<DeclaredCapability> DeclareCapabilities() => [];

		public Task<CapabilityInvocationResult> InvokeAsync(
			CapabilityInvocation invocation,
			CancellationToken cancellationToken)
		{
			invocations.Add($"{invocation.Kind}/{invocation.Operation}");

			return Task.FromResult(CapabilityInvocationResult.Ok(JsonDocument.Parse(describeJson).RootElement.Clone()));
		}
	}
}
