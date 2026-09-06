using MacroDeck.Sdk.Actions;

namespace MacroDeck.Sdk.Tests.UnitTests;

[TestFixture]
internal sealed class ActionParameterVisibilityTests
{
	private static readonly string[] _header = ["header"];
	private static readonly string[] _allAuthKinds = ["basic", "bearer", "header"];

	[Test]
	public void A_parameter_is_visible_by_default()
	{
		Assert.That(ActionParameter.Text("field").VisibleWhen, Is.Null);
	}

	[Test]
	public void Only_when_records_the_sibling_and_its_values()
	{
		var parameter = ActionParameter.Text("headerName").OnlyWhen("authType", "header");

		Assert.Multiple(() =>
		{
			Assert.That(parameter.VisibleWhen, Is.Not.Null);
			Assert.That(parameter.VisibleWhen!.ParameterName, Is.EqualTo("authType"));
			Assert.That(parameter.VisibleWhen.Values, Is.EqualTo(_header));
		});
	}

	[Test]
	public void Only_when_accepts_several_values()
	{
		var parameter = ActionParameter.Secret("secret").OnlyWhen("authType", "basic", "bearer", "header");

		Assert.That(parameter.VisibleWhen!.Values, Is.EqualTo(_allAuthKinds));
	}

	/// <summary>
	/// OnlyWhen copies rather than mutating, so a shared parameter instance cannot pick up a condition
	/// from an unrelated action that reused it.
	/// </summary>
	[Test]
	public void Only_when_leaves_the_original_untouched()
	{
		var original = ActionParameter.Text("field");
		var conditional = original.OnlyWhen("mode", "advanced");

		Assert.Multiple(() =>
		{
			Assert.That(original.VisibleWhen, Is.Null);
			Assert.That(conditional, Is.Not.SameAs(original));
			Assert.That(conditional.VisibleWhen, Is.Not.Null);
		});
	}

	[Test]
	public void Only_when_preserves_every_other_property()
	{
		var parameter = ActionParameter
			.Choice("mode",
				options: [new ActionParameterOption { Value = "a" }],
				label: "Mode",
				description: "Pick one",
				defaultValue: "a",
				required: true)
			.OnlyWhen("other", "x");

		Assert.Multiple(() =>
		{
			Assert.That(parameter.Name, Is.EqualTo("mode"));
			Assert.That(parameter.Type, Is.EqualTo(ActionParameterType.Choice));
			Assert.That(parameter.Label.Literal, Is.EqualTo("Mode"));
			Assert.That(parameter.Description.Literal, Is.EqualTo("Pick one"));
			Assert.That(parameter.DefaultValue, Is.EqualTo("a"));
			Assert.That(parameter.Required, Is.True);
			Assert.That(parameter.Options, Has.Count.EqualTo(1));
		});
	}
}
