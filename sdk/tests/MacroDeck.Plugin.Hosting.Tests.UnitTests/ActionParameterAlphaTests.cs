using MacroDeck.Plugin.Hosting.Capabilities.Actions;
using MacroDeck.Sdk.Actions;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class ActionParameterAlphaTests
{
	[Test]
	public void A_colour_parameter_announces_alpha_only_when_the_plugin_opts_in()
	{
		var plain = ActionParameterMapper.ToDto(ActionParameter.Color("color"));
		var optedIn = ActionParameterMapper.ToDto(
			new ActionParameter { Name = "color", Type = ActionParameterType.Color, AllowAlpha = true });

		Assert.Multiple(() =>
		{
			Assert.That(plain.AllowAlpha, Is.Null);
			Assert.That(optedIn.AllowAlpha, Is.True);
		});
	}
}
