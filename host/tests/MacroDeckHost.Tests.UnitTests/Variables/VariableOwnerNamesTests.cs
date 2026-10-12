using MacroDeckHost.Application.Variables;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
public class VariableOwnerNamesTests
{
	[TestCase(null)]
	[TestCase("")]
	public async Task A_variable_without_an_owner_leaves_the_name_empty(string? integrationId)
	{
		var names = new VariableOwnerNames(new ConfigurableIntegrationRegistry([]), TestLocalization.ScopeFactory);

		Assert.That(await names.Resolve(integrationId), Is.Empty);
	}
}
