using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Integrations;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Identity;

namespace MacroDeckHost.Tests.UnitTests.Integrations;

[TestFixture]
internal sealed class ShippedIntegrationIdsTests
{
	private static List<IIntegration> Discovered()
		=> IntegrationDiscovery.DiscoverIntegrations(Serilog.Log.Logger).ToList();

	[Test]
	public void Discovery_returns_the_shipped_integrations()
	{
		Assert.That(Discovered(), Has.Count.GreaterThanOrEqualTo(15));
	}

	// Issue #789 retired the built-in Teams integration; it returns as a standalone plugin, never as a
	// built-in. Discovery is reflection-based, so re-adding the type anywhere in the assembly would
	// silently ship it again.
	[Test]
	public void Teams_is_not_a_shipped_integration()
	{
		var ids = Discovered().Select(integration => integration.Id).ToList();

		Assert.That(ids, Has.No.Member("app.macro-deck.teams"));
	}

	[Test]
	public void Every_shipped_integration_passes_capability_validation()
	{
		var failures = Discovered()
			.Select(integration => new
			{
				integration.Id,
				Type = integration.GetType().Name,
				Conflicts = IntegrationCapabilityValidator.Validate(integration)
			})
			.Where(result => result.Conflicts.Count > 0)
			.Select(result => $"{result.Type} ({result.Id}): " +
				string.Join("; ", result.Conflicts.Select(conflict => conflict.ToString())))
			.ToList();

		Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
	}

	[Test]
	public void Every_shipped_integration_id_is_a_package_id()
	{
		var invalid = Discovered()
			.Where(integration => !MacroDeckId.IsValidOwnerId(integration.Id, OwnerIdKind.Package))
			.Select(integration => integration.Id)
			.ToList();

		Assert.That(invalid, Is.Empty, "integration ids must be reverse-domain");
	}

	[Test]
	public void Shipped_integration_ids_are_unique()
	{
		var duplicates = Discovered()
			.GroupBy(integration => integration.Id, StringComparer.Ordinal)
			.Where(group => group.Count() > 1)
			.Select(group => group.Key)
			.ToList();

		Assert.That(duplicates, Is.Empty);
	}
}
