using MacroDeck.Plugin.Hosting.Capabilities.Actions;
using MacroDeck.Sdk.Actions;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class ActionParameterSearchableTests
{
	[Test]
	public void A_choice_announces_search_only_when_the_plugin_opts_in()
	{
		var options = new[] { new ActionParameterOption { Value = "a" } };
		var plain = ActionParameterMapper.ToDto(ActionParameter.Choice("counter", options));
		var optedIn = ActionParameterMapper.ToDto(ActionParameter.Choice("counter", options).AsSearchable());

		Assert.Multiple(() =>
		{
			Assert.That(plain.Searchable, Is.Null);
			Assert.That(optedIn.Searchable, Is.True);
		});
	}

	[Test]
	public void Opting_in_keeps_everything_the_factory_set_and_leaves_the_original_untouched()
	{
		var original = ActionParameter.DynamicChoice("counter", "Counter", required: true)
			.OnlyWhen("mode", "pick");

		var searchable = original.AsSearchable();

		Assert.Multiple(() =>
		{
			Assert.That(searchable.Searchable, Is.True);
			Assert.That(original.Searchable, Is.False);
			Assert.That(searchable.Type, Is.EqualTo(ActionParameterType.DynamicChoice));
			Assert.That(searchable.DynamicOptions, Is.True);
			Assert.That(searchable.Required, Is.True);
			Assert.That(searchable.VisibleWhen?.ParameterName, Is.EqualTo("mode"));
		});
	}
}
