using MacroDeck.Localization;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Application.Variables;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
public class VariableDeclarationFactoryTests
{
	[Test]
	public void A_blank_configuration_key_is_treated_as_no_configuration()
	{
		var declared = VariableDefinition.Eager("obs_mac_current_scene", VariableType.Text) with
		{
			DisplayName = LocalizedText.FromLiteral("Current scene"),
			Configuration = new VariableConfiguration("   ", LocalizedText.FromLiteral("Mac"))
		};

		var presentation = VariableDeclarationFactory.From(declared).Presentation;

		Assert.That(presentation, Is.Not.Null);
		Assert.Multiple(() =>
		{
			// The display name survives; only the unusable configuration is dropped, so a bad group label
			// never costs the user the variable itself.
			Assert.That(presentation!.DisplayName.Literal, Is.EqualTo("Current scene"));
			Assert.That(presentation.ConfigurationKey, Is.Null);
			Assert.That(presentation.ConfigurationName.IsEmpty, Is.True);
		});
	}

	[Test]
	public void Neither_a_display_name_nor_a_usable_configuration_yields_no_presentation_at_all()
	{
		var declared = VariableDefinition.Eager("obs_mac_current_scene", VariableType.Text) with
		{
			Configuration = new VariableConfiguration("", default)
		};

		Assert.That(VariableDeclarationFactory.From(declared).Presentation, Is.Null);
	}

	/// <summary>The open map is readable from a template and never interpreted, so an entry that would
	/// shadow one of the typed attributes the host computes itself is dropped rather than allowed to
	/// override it.</summary>
	[Test]
	public void An_open_attribute_under_a_reserved_key_is_dropped()
	{
		var declared = VariableDefinition.Eager("obs_mac_current_scene", VariableType.Text) with
		{
			Unit = "%",
			Attributes = new Dictionary<string, string>
			{
				["unit"] = "dB", ["semantic_kind"] = "duration", ["friendly_name"] = "Current scene"
			}
		};

		var declaration = VariableDeclarationFactory.From(declared);

		Assert.Multiple(() =>
		{
			Assert.That(declaration.Unit, Is.EqualTo("%"));
			Assert.That(declaration.Attributes, Has.Count.EqualTo(1));
			Assert.That(declaration.Attributes!["friendly_name"], Is.EqualTo("Current scene"));
		});
	}

	/// <summary>An unbounded or malformed open map must cost the provider the offending entries, never the
	/// working variable - every attribute rides the variable broadcast.</summary>
	[Test]
	public void An_oversized_or_malformed_attribute_map_is_clamped_rather_than_rejected()
	{
		var attributes = Enumerable.Range(0, VariableLimits.MaxAttributeEntries + 10)
			.ToDictionary(index => $"key_{index}", _ => "value");
		attributes["Bad-Key"] = "value";
		attributes["too_long"] = new string('x', VariableLimits.MaxAttributeValueLength + 1);

		var declared = VariableDefinition.Eager("obs_mac_current_scene", VariableType.Text) with
		{
			Attributes = attributes
		};

		var declaration = VariableDeclarationFactory.From(declared);

		Assert.Multiple(() =>
		{
			Assert.That(declaration.Attributes, Has.Count.EqualTo(VariableLimits.MaxAttributeEntries));
			Assert.That(declaration.Attributes, Does.Not.ContainKey("Bad-Key"));
			Assert.That(declaration.Attributes, Does.Not.ContainKey("too_long"));
		});
	}
}
