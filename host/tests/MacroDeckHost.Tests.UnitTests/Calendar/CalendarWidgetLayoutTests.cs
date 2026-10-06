using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Model.Nodes;
using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Widgets;
using static MacroDeckHost.Tests.UnitTests.Calendar.CalendarTesting;
using static MacroDeckHost.Tests.UnitTests.Calendar.CalendarTree;

namespace MacroDeckHost.Tests.UnitTests.Calendar;

[TestFixture]
public class CalendarWidgetLayoutTests
{
	private CalendarWidgetHarness _harness = null!;

	[SetUp]
	public async Task SetUp()
	{
		_harness = new CalendarWidgetHarness().With(Event("review", Noon.AddHours(1), TimeSpan.FromHours(1), title: "Review"));
		await _harness.SyncAsync();
	}

	[TestCase(1, 1, true, "glance")]
	[TestCase(1, 2, true, "glance")]
	[TestCase(2, 1, true, "dateCompact")]
	[TestCase(2, 2, true, "dateSections")]
	[TestCase(3, 2, true, "dateSections")]
	[TestCase(1, 1, false, "compactNarrow")]
	[TestCase(1, 2, false, "compactNarrow")]
	[TestCase(2, 1, false, "compact")]
	[TestCase(2, 2, false, "sections")]
	public async Task The_agenda_picks_its_layout_for_the_tile_size(int columns, int rows, bool showDate, string layout)
	{
		await using var session = await _harness.OpenWidgetAsync(CalendarWidgetTypes.LayoutAgenda,
			new { days = 2, showDate });

		Assert.That(Chosen(session.BuildTree().Root, columns, rows), Does.EndWith("." + layout));
	}

	[TestCase(1, 1, "eventNarrow")]
	[TestCase(2, 1, "event")]
	[TestCase(2, 2, "event")]
	public async Task The_next_event_layout_picks_its_layout_for_the_tile_size_with_or_without_the_date(
		int columns,
		int rows,
		string layout)
	{
		await using var dated = await _harness.OpenWidgetAsync(CalendarWidgetTypes.LayoutNextEvent, new { });
		await using var plain = await _harness.OpenWidgetAsync(CalendarWidgetTypes.LayoutNextEvent,
			new { showDate = false });

		Assert.Multiple(() =>
		{
			Assert.That(Chosen(dated.BuildTree().Root, columns, rows), Does.EndWith("." + layout));
			Assert.That(Chosen(plain.BuildTree().Root, columns, rows), Does.EndWith("." + layout));
		});
	}

	[TestCase(CalendarWidgetTypes.LayoutAgenda)]
	[TestCase(CalendarWidgetTypes.LayoutNextEvent)]
	public async Task A_full_week_stays_within_the_ui_tree_limits(string widgetLayout)
	{
		var harness = new CalendarWidgetHarness();

		for (var index = 0; index < 120; index++)
		{
			harness.With(Event("busy" + index, Noon.AddHours(index), TimeSpan.FromMinutes(45),
				title: new string('x', 120)) with { Location = new string('y', 120) });
		}

		await harness.SyncAsync();

		await using var session = await harness.OpenWidgetAsync(widgetLayout,
			new { days = 7, showLocation = true, showCalendar = true });
		var tree = session.BuildTree();

		Assert.Multiple(() =>
		{
			Assert.That(UiCanonicalJson.SerializeToUtf8Bytes(tree).Length, Is.LessThanOrEqualTo(ProtocolLimits.MaxUiTreeBytes));
			Assert.That(Flatten(tree.Root).Count(), Is.LessThanOrEqualTo(ProtocolLimits.MaxUiNodesPerTree));
		});
	}

	private static string Chosen(UiNode root, int columns, int rows)
	{
		var layout = Get(root, "layout");
		var index = UiResponsiveSelection.SelectChild(layout.Properties[UiComponentProperties.Variants],
			layout.Children.Count,
			InnerCells(columns, columns, rows),
			InnerCells(rows, columns, rows));

		return layout.Children[index].Id;
	}

	private static double InnerCells(int cells, int columns, int rows)
	{
		var padding = WidgetSafeArea.LengthFor(WidgetSafeArea.DefaultCornerRadius);
		var basis = Math.Min(Extent(columns), Extent(rows));
		var inset = Math.Min(padding.Basis * basis, padding.MaxOfCell!.Value * UiLength.Cell);

		return (Extent(cells) - (2 * inset)) / UiLength.Cell;
	}

	private static double Extent(int cells) => (cells * UiLength.Cell) + ((cells - 1) * GridDefaults.WidgetSpacing);
}
