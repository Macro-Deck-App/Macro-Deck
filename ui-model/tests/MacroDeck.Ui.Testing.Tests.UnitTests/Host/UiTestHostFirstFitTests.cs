using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Surfaces;

namespace MacroDeck.Ui.Testing.Tests.UnitTests.Host;

[TestFixture]
public class UiTestHostFirstFitTests
{
	private static readonly string[] _everyLayout = ["group.inline.name", "group.stacked.name"];

	[Test]
	public void Queries_see_every_layout_because_the_host_cannot_measure_text()
	{
		var host = UiTestHost.Render(new UiFirstFit
			{
				Key = "group",
				Children =
				[
					new UiStack { Key = "inline", Children = [new UiTextRun { Key = "name", Text = "Mouse" }] },
					new UiStack { Key = "stacked", Children = [new UiTextRun { Key = "name", Text = "Mouse" }] },
				],
			},
			new UiSurface { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared });

		Assert.That(host.ByText("Mouse").Select(node => node.Id), Is.EqualTo(_everyLayout));
	}
}
