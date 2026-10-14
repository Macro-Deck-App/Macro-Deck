using MacroDeckHost.Integrations.Ui;
using MacroDeckHost.Widgets;

namespace MacroDeckHost.Tests.UnitTests.Integrations;

[TestFixture]
internal sealed class WidgetBackgroundColorTests
{
	[TestCase(null)]
	[TestCase("")]
	[TestCase("   ")]
	[TestCase("transparent")]
	[TestCase(" Transparent ")]
	[TestCase("#abc")]
	[TestCase("#ABCD")]
	[TestCase("#1e90ff")]
	[TestCase("#1E90FF80")]
	[TestCase("#12345")]
	[TestCase("#ggg")]
	[TestCase("red")]
	[TestCase("RebeccaPurple")]
	[TestCase("notacolor")]
	[TestCase("rgb(255, 0, 0)")]
	[TestCase("rgb(100% 50% 0%)")]
	[TestCase("rgba(10, 20, 30, 0.5)")]
	[TestCase("rgb(10 20 30 / 25%)")]
	[TestCase("rgba(300, -5, 12.6, 2)")]
	[TestCase("rgb(1, 2)")]
	[TestCase("var(--color-accent)")]
	public void A_stored_background_reads_as_the_host_widgets_read_it(string? stored)
	{
		Assert.That(WidgetBackgroundColor.Normalize(stored), Is.EqualTo(WidgetColor.NormalizeBackground(stored)));
	}

	[TestCase("transparent", "transparent")]
	[TestCase("#ABC", "#aabbcc")]
	[TestCase("rgba(255, 0, 0, 0.5)", "#ff000080")]
	[TestCase("var(--color-accent)", null)]
	public void A_stored_background_normalizes_to_a_widget_colour(string stored, string? expected)
	{
		Assert.That(WidgetBackgroundColor.Normalize(stored), Is.EqualTo(expected));
	}
}
