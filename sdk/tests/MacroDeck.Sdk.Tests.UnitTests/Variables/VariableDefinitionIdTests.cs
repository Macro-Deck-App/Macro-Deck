using MacroDeck.Sdk.Variables;

namespace MacroDeck.Sdk.Tests.UnitTests.Variables;

[TestFixture]
public class VariableDefinitionIdTests
{
	[Test]
	public void FromName_swaps_underscores_for_hyphens()
		=> Assert.That(VariableDefinitionId.FromName("system_volume_percent"), Is.EqualTo("system-volume-percent"));

	[Test]
	public void FromName_of_a_template_name_is_null()
	{
		var templateName = $"twitch_{VariableNameTemplate.Placeholder("account")}_is_connected";

		Assert.That(VariableDefinitionId.FromName(templateName), Is.Null);
	}

	[Test]
	public void FromName_of_null_is_null()
		=> Assert.That(VariableDefinitionId.FromName(null), Is.Null);

	[Test]
	public void FromName_of_empty_is_null()
		=> Assert.That(VariableDefinitionId.FromName(""), Is.Null);

	[Test]
	public void FromName_returns_null_rather_than_an_invalid_id_when_the_swap_still_does_not_validate()
		=> Assert.That(VariableDefinitionId.FromName("a__b"), Is.Null);
}
