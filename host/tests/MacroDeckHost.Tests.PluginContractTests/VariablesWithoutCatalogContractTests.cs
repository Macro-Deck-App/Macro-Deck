using MacroDeck.Plugin.Hosting.Capabilities.Variables;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Tests.PluginContractTests.Harness;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Tests.PluginContractTests;

/// <summary>
/// A plugin whose provider declares only eager variables and no catalog must be unaffected by the
/// catalog half ADR 0081 merged into the same contract: every eager operation still round-trips exactly
/// as before, the integration overview lists the one kind it declared, and the catalog members degrade to
/// their documented empty/null shapes rather than faulting - the base adapter implements
/// <see cref="IVariableProvider" /> unconditionally (ADR 0004), so the cast has to answer safely
/// even for a caller that has not read the declared-kinds gate.
/// </summary>
[TestFixture]
internal sealed class VariablesWithoutCatalogContractTests : CapabilityContractFixture
{
	private static DeclaredCapability Variable(string localId)
		=> new()
		{
			Kind = CapabilityKinds.Variables, LocalId = localId,
			VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 2 }
		};

	private static VariablesCapabilityHandler Handler(IPluginIntegration provider)
		=> new([provider],
			TestMetadata.Default,
			new VariableSubscriptions(Serilog.Log.Logger),
			Serilog.Core.Logger.None);

	[Test]
	public async Task A_catalog_less_plugin_round_trips_its_eager_variables_exactly_as_before()
	{
		var variable = VariableDefinition.Eager("cpu_temp", VariableType.Numeric, decimalPlaces: 1)
			with
			{
				Id = "cpu-temp"
			};
		var integration = await ConnectAsync([
				Handler(new TestVariableIntegration([variable],
					dependsOnConfiguration: true,
					read: (_, _) => ValueTask.FromResult(VariableReading.Of(42.5))))
			],
			[Variable("cpu-temp")],
			[CapabilityKinds.Variables]);

		var provider = (IVariableProvider)integration;

		Assert.Multiple(() =>
		{
			Assert.That(provider.DeclaredVariables.Select(v => v.Name), Is.EqualTo(new[] { "cpu_temp" }));
			Assert.That(provider.Variables.Single().DecimalPlaces, Is.EqualTo(1));
			Assert.That(provider.VariablesDependOnConfiguration, Is.True);
			Assert.That(IntegrationCapabilityValidator.Validate(integration), Is.Empty);
		});

		var value = (await provider.ReadAsync("cpu-temp", CancellationToken.None)).Value;
		Assert.That(value, Is.EqualTo(42.5));
	}

	[Test]
	public async Task The_capability_catalog_lists_variables_once()
	{
		var variable = VariableDefinition.Eager("cpu_temp", VariableType.Numeric) with { Id = "cpu-temp" };
		var integration = await ConnectAsync([Handler(new TestVariableIntegration([variable]))],
			[Variable("cpu-temp")],
			[CapabilityKinds.Variables]);

		var kinds = ProvidedCapabilityCatalog.For(integration).Select(capability => capability.Kind).ToList();

		Assert.Multiple(() =>
		{
			// One entry per integration, whether or not it also browses a catalog - which is the UI half of
			// ADR 0081's "one provider contract": the Variables page must not list the same integration
			// twice.
			Assert.That(kinds.Count(kind => kind == CapabilityKinds.Variables), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task The_catalog_members_degrade_rather_than_fault()
	{
		var variable = VariableDefinition.Eager("cpu_temp", VariableType.Numeric) with { Id = "cpu-temp" };
		var integration = await ConnectAsync([Handler(new TestVariableIntegration([variable]))],
			[Variable("cpu-temp")],
			[CapabilityKinds.Variables]);

		var provider = (IVariableProvider)integration;

		var page = await provider.DiscoverAsync(new VariableCatalogQuery(), CancellationToken.None);
		var resolved = await provider.ResolveAsync("anything", CancellationToken.None);
		var subscribed = await provider.SubscribeAsync(["anything"], CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(provider.SupportsCatalog, Is.False);
			Assert.That(page.Items, Is.Empty);
			Assert.That(resolved, Is.Null);
			Assert.That(subscribed, Is.Empty);
			Assert.That(IntegrationCapabilityValidator.Validate(integration),
				Is.Empty,
				"the session must not be degraded by exercising a half this plugin never declared");
		});
	}
}
