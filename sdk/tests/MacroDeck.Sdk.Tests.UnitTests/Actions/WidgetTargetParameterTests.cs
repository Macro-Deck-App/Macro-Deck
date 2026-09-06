using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Widgets;

namespace MacroDeck.Sdk.Tests.UnitTests.Actions;

[TestFixture]
public class WidgetTargetParameterTests
{
	private static readonly string[] _actionButtonOnly = ["ActionButton"];

	/// <summary>
	/// The call every shipped integration already makes. Turning <c>$self</c> off by default would be the
	/// tempting way to make a configuration surface behave, and would silently remove the "This widget"
	/// choice from every widget action that exists.
	/// </summary>
	[Test]
	public void A_widget_target_offers_this_widget_unless_it_is_asked_not_to()
	{
		var parameter = ActionParameter.WidgetTarget("target");

		Assert.Multiple(() =>
		{
			Assert.That(parameter.Type, Is.EqualTo(ActionParameterType.WidgetTarget));
			Assert.That(parameter.OptionsSourceId, Is.EqualTo(WidgetOptionsSources.Widgets));
			Assert.That(parameter.DefaultValue, Is.EqualTo(WidgetTargets.Self));
			Assert.That(parameter.Required, Is.True);
			Assert.That(parameter.AllowSelf, Is.True);
			Assert.That(parameter.WidgetTypes, Is.Empty);
		});
	}

	[Test]
	public void A_widget_target_without_this_widget_has_no_default_to_fall_back_on()
	{
		var parameter = ActionParameter.WidgetTarget("target",
			new WidgetTargetOptions { AllowSelf = false, WidgetTypes = _actionButtonOnly });

		Assert.Multiple(() =>
		{
			Assert.That(parameter.AllowSelf, Is.False);
			Assert.That(parameter.DefaultValue, Is.Null);
			Assert.That(parameter.WidgetTypes, Is.EqualTo(_actionButtonOnly));
		});
	}
}
