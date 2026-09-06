using MacroDeckHost.Integrations.HomeAssistant;

namespace MacroDeckHost.Tests.UnitTests.HomeAssistant;

[TestFixture]
internal sealed class HomeAssistantVariablesTests
{
	[Test]
	public void An_entity_id_becomes_a_kebab_case_definition_id_and_a_snake_case_name()
	{
		var names = HomeAssistantVariableNames.Build(["light.kitchen_lamp"]);

		Assert.Multiple(() =>
		{
			Assert.That(names[0].StateName, Is.EqualTo("ha_light_kitchen_lamp"));
			Assert.That(names[0].StateDefinitionId, Is.EqualTo("entity-light-kitchen-lamp"));
			Assert.That(names[0].AttributesName, Is.EqualTo("ha_light_kitchen_lamp_attributes"));
			Assert.That(names[0].AttributesDefinitionId, Is.EqualTo("entity-light-kitchen-lamp-attributes"));
		});
	}

	[Test]
	public void A_definition_id_never_exceeds_the_sixty_four_character_cap()
	{
		var longId = "sensor." + new string('a', 120);

		var names = HomeAssistantVariableNames.Build([longId]);

		Assert.Multiple(() =>
		{
			Assert.That(names[0].StateDefinitionId.Length, Is.LessThanOrEqualTo(64));
			Assert.That(names[0].AttributesDefinitionId.Length, Is.LessThanOrEqualTo(64));
			Assert.That(names[0].StateDefinitionId, Does.Match(@"^[a-z][a-z0-9]*(-[a-z0-9]+)*$"));
		});
	}

	[Test]
	public void A_collision_suffix_is_applied_to_both_the_name_and_the_definition_id()
	{
		var names = HomeAssistantVariableNames.Build(["light.a_b", "light.a-b"]);

		Assert.Multiple(() =>
		{
			Assert.That(names[0].StateDefinitionId, Is.EqualTo("entity-light-a-b"));
			Assert.That(names[1].StateDefinitionId, Is.EqualTo("entity-light-a-b-2"));
			Assert.That(names[1].AttributesDefinitionId, Is.EqualTo("entity-light-a-b-2-attributes"));
			Assert.That(names[1].StateName, Is.EqualTo("ha_light_a_b_2"));
			Assert.That(names[0].StateDefinitionId, Is.Not.EqualTo(names[1].StateDefinitionId));
			Assert.That(names[0].AttributesDefinitionId, Is.Not.EqualTo(names[1].AttributesDefinitionId));
		});
	}

	[Test]
	public void Build_is_deterministic_for_the_same_selection()
	{
		var first = HomeAssistantVariableNames.Build(["light.a", "light.b"]);
		var second = HomeAssistantVariableNames.Build(["light.a", "light.b"]);

		Assert.Multiple(() =>
		{
			Assert.That(first[0].StateDefinitionId, Is.EqualTo(second[0].StateDefinitionId));
			Assert.That(first[1].StateDefinitionId, Is.EqualTo(second[1].StateDefinitionId));
		});
	}

	[Test]
	public void Blank_entries_are_skipped()
	{
		var names = HomeAssistantVariableNames.Build(["light.a", "", "  "]);

		Assert.That(names, Has.Count.EqualTo(1));
	}
}
