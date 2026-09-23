using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Surfaces;

namespace MacroDeck.Ui.Testing.Tests.UnitTests.Host;

[TestFixture]
public class UiTestHostResponsiveTests
{
	private static readonly string[] _compactOnly = ["weather.compact"];

	private static UiTestHost RenderWeather()
		=> UiTestHost.Render(new UiResponsive
			{
				Key = "weather",
				Default = new UiTextRun { Key = "compact", Text = "21°" },
				Variants =
				[
					new UiResponsiveVariant { MinWidth = 1.5, Content = new UiTextRun { Key = "wide", Text = "21° Sunny" } },
					new UiResponsiveVariant { MaxAspect = 0.67, Content = new UiTextRun { Key = "tall", Text = "21° tall" } },
				],
			},
			new UiSurface { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared });

	[Test]
	public void Without_a_box_queries_see_only_the_default_layout_and_never_its_fallback_copy()
	{
		var host = RenderWeather();

		Assert.Multiple(() =>
		{
			Assert.That(host.ByText("21°").Select(node => node.Id), Is.EqualTo(_compactOnly));
			Assert.That(host.SingleByType(UiComponents.Text).Id, Is.EqualTo("weather.compact"));
		});
	}

	[Test]
	public void A_box_selects_the_layout_a_reader_would_draw_and_a_new_box_selects_again()
	{
		var host = RenderWeather();

		host.SetBox(2.1, 1);
		var wide = host.SingleByType(UiComponents.Text).Id;
		host.SetBox(1, 2.1);
		var tall = host.SingleByType(UiComponents.Text).Id;

		Assert.Multiple(() =>
		{
			Assert.That(wide, Is.EqualTo("weather.wide"));
			Assert.That(tall, Is.EqualTo("weather.tall"));
		});
	}

	[Test]
	public void Lookups_by_id_still_reach_layouts_that_are_not_shown()
	{
		var host = RenderWeather();

		Assert.Multiple(() =>
		{
			Assert.That(host.FindById("weather.wide"), Is.Not.Null);
			Assert.That(host.ById("weather._fallback.compact").Text("text"), Is.EqualTo("21°"));
		});
	}
}
