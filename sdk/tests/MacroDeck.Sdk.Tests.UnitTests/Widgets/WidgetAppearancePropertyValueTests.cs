using MacroDeck.Sdk.Widgets;

namespace MacroDeck.Sdk.Tests.UnitTests.Widgets;

[TestFixture]
internal sealed class WidgetAppearancePropertyValueTests
{
	[TestCase(WidgetAppearanceProperty.BackgroundColor, 0)]
	[TestCase(WidgetAppearanceProperty.Label, 1)]
	[TestCase(WidgetAppearanceProperty.LabelColor, 2)]
	[TestCase(WidgetAppearanceProperty.Icon, 3)]
	[TestCase(WidgetAppearanceProperty.Font, 4)]
	[TestCase(WidgetAppearanceProperty.Border, 5)]
	[TestCase(WidgetAppearanceProperty.BorderColor, 6)]
	[TestCase(WidgetAppearanceProperty.IconDisplay, 7)]
	[TestCase(WidgetAppearanceProperty.IconColor, 9)]
	public void Every_value_keeps_the_number_plugins_and_stored_data_already_use(
		WidgetAppearanceProperty property,
		int expected)
	{
		Assert.That((int)property, Is.EqualTo(expected));
	}

	[Test]
	public void An_icon_color_alone_is_a_change()
	{
		Assert.That(new WidgetAppearancePatch { IconColor = "#ef4444" }.IsEmpty, Is.False);
	}
}
