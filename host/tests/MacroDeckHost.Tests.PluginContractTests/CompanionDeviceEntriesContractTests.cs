using MacroDeck.Plugin.Hosting.Capabilities.CompanionDeviceEntries;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk.Companion;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Tests.PluginContractTests.Harness;

namespace MacroDeckHost.Tests.PluginContractTests;

[TestFixture]
internal sealed class CompanionDeviceEntriesContractTests : CapabilityContractFixture
{
	[Test]
	public async Task A_plugin_asking_for_device_entries_is_known_to_declare_them_without_a_capability_row()
	{
		var integration = await ConnectAsync(
			[new CompanionDeviceEntriesCapabilityHandler([new DeviceEntriesIntegration()])],
			[
				new DeclaredCapability
				{
					Kind = CapabilityKinds.CompanionDeviceEntries,
					LocalId = ProviderCapabilityId.LocalId,
					VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
				}
			],
			[CapabilityKinds.CompanionDeviceEntries]);

		Assert.Multiple(() =>
		{
			Assert.That(((IDeclaredCapabilityKinds)integration).DeclaredCapabilityKinds,
				Does.Contain(CapabilityKinds.CompanionDeviceEntries));
			Assert.That(ProvidedCapabilityCatalog.For(integration).Select(capability => capability.Kind),
				Does.Not.Contain(CapabilityKinds.CompanionDeviceEntries));
		});
	}

	[Test]
	public async Task A_plugin_built_before_device_entries_declares_nothing_new()
	{
		var integration = await ConnectAsync([
				new MacroDeck.Plugin.Hosting.Capabilities.Actions.ActionsCapabilityHandler([
					new TestIntegration(new TestAction("play"))
				])
			],
			[
				new DeclaredCapability
				{
					Kind = CapabilityKinds.Actions,
					LocalId = "play",
					VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
				}
			],
			[CapabilityKinds.Actions]);

		Assert.That(((IDeclaredCapabilityKinds)integration).DeclaredCapabilityKinds,
			Does.Not.Contain(CapabilityKinds.CompanionDeviceEntries));
	}

	private sealed class DeviceEntriesIntegration : MacroDeck.Sdk.IPluginIntegration, ICompanionDeviceConfigEntries
	{
		public IReadOnlyList<MacroDeck.Sdk.Actions.IActionDefinition> Actions => [];

		public Task InitializeAsync(MacroDeck.Sdk.IIntegrationContext context)
		{
			return Task.CompletedTask;
		}

		public Task ShutdownAsync()
		{
			return Task.CompletedTask;
		}
	}
}
