using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Testing;
using MacroDeck.Ui.Components;
using MacroDeckHost.Widgets.HistoryGraph;
using static MacroDeckHost.Tests.UnitTests.Widgets.Ui.HistoryGraphTestSupport;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

/// <summary>
/// The card's composition, which is what a second renderer has to reproduce: three layers with the chart
/// behind, type that does not grow with the widget, and nothing in the tree a client would have to be
/// taught about this widget in particular.
/// </summary>
[TestFixture]
public class HistoryGraphWidgetViewTests
{
	private static readonly double[] _quarters = [25d, 50d, 75d];

	private static readonly double[] _nothing = [];

	private static readonly double[] _expectedQuarters = [0.25, 0.5, 0.75];

	private static readonly string[] _expectedLayers =
		[UiComponents.Chart, UiComponents.Stack, UiComponents.Stack];

	private static readonly object _default = new
	{
		valueVariable = Metric, title = "CPU Load", subtitleVariable = Caption, maxValue = 100,
	};

	[Test]
	public void The_card_is_three_layers_with_the_chart_behind_the_rest()
	{
		var host = Render(_default);

		Assert.Multiple(() =>
		{
			Assert.That(host.Root.Type, Is.EqualTo(UiComponents.Layer));
			Assert.That(host.Root.Children.Select(child => child.Type),
				Is.EqualTo(_expectedLayers).AsCollection,
				"the chart is drawn first, so the labels and the value sit over it rather than under it");
		});
	}

	[Test]
	public void The_value_is_centred_on_the_whole_card_rather_than_on_what_the_labels_leave_over()
	{
		var value = Render(_default).ById("historyGraph.valueLayer");

		Assert.Multiple(() =>
		{
			Assert.That(value.Text(UiComponentProperties.Justify), Is.EqualTo(UiComponentJustify.Center));
			Assert.That(value.HasProperty(UiComponentProperties.Padding),
				Is.False,
				"padding would inset the value from the card's own centre line and narrow a long reading");
		});
	}

	[Test]
	public void The_value_and_its_unit_share_a_baseline()
	{
		var row = Render(_default, unit: "%").ById("historyGraph.valueLayer.valueRow");

		Assert.That(row.Text(UiComponentProperties.Align),
			Is.EqualTo(UiComponentAlignments.Baseline),
			"two runs at different sizes only look level when their text is level, not their boxes");
	}

	[Test]
	public void Type_stops_growing_once_the_widget_passes_one_cell()
	{
		var host = Render(_default, unit: "%");

		var sizes = new[]
		{
			host.ById("historyGraph.labels.title").Property(UiComponentProperties.Size),
			host.ById("historyGraph.labels.subtitle").Property(UiComponentProperties.Size),
			host.ById("historyGraph.valueLayer.valueRow.value").Property(UiComponentProperties.Size),
			host.ById("historyGraph.valueLayer.valueRow.unit").Property(UiComponentProperties.Size),
		};

		Assert.That(sizes.Select(size => size?.TryGetProperty("maxOfCell", out _)),
			Has.All.EqualTo(true),
			"a nine-cell card must read as a large chart with a caption, not as a poster");
	}

	[Test]
	public void A_run_the_user_did_not_configure_is_absent_rather_than_empty()
	{
		var host = Render(new { valueVariable = Metric, maxValue = 100 });

		Assert.Multiple(() =>
		{
			Assert.That(host.FindById("historyGraph.labels.title"), Is.Null);
			Assert.That(host.FindById("historyGraph.valueLayer.valueRow.unit"),
				Is.Null,
				"an empty run still takes its own height and the gap in front of it");
		});
	}

	[Test]
	public void The_chart_carries_a_colour_only_when_the_user_chose_one()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Render(_default).ById("historyGraph.chart").HasProperty(UiComponentProperties.Color),
				Is.False,
				"absence is how this profile spells 'the reader's own accent', so a theme change repaints it");
			Assert.That(Render(new { valueVariable = Metric, accentColor = "#ff6b35" })
					.ById("historyGraph.chart")
					.Text(UiComponentProperties.Color),
				Is.EqualTo("#ff6b35"));
		});
	}

	[Test]
	public void A_reset_accent_colour_leaves_the_chart_on_the_readers_own_accent()
	{
		// What the editor's reset cell stores (issue #896). It has to reach the tree as an absent colour,
		// which is this profile's spelling of "the reader's own accent" - an empty colour key would not be.
		var chart = Render(new { valueVariable = Metric, accentColor = string.Empty }).ById("historyGraph.chart");

		Assert.That(chart.HasProperty(UiComponentProperties.Color), Is.False);
	}

	[Test]
	public void The_editors_accent_reset_sentinel_never_reaches_the_tree()
	{
		var chart = Render(new { valueVariable = Metric, accentColor = "var(--color-accent)" })
			.ById("historyGraph.chart");

		Assert.That(chart.HasProperty(UiComponentProperties.Color),
			Is.False,
			"a widget profile colour is #rrggbb; the client's own token must normalise to absent");
	}

	[Test]
	public void The_series_travels_as_fractions_of_the_band_and_nothing_else()
	{
		var state = new UiState<HistoryGraphViewState>(Resolver(_default).Resolve(_quarters));
		var host = UiTestHost.Render(HistoryGraphWidgetView.Build(state, Config(_default)),
			new UiSurface { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared });

		var points = host.ById("historyGraph.chart").Property(UiComponentProperties.Points)!.Value;

		Assert.Multiple(() =>
		{
			Assert.That(points.EnumerateArray().Select(point => point.GetDouble()),
				Is.EqualTo(_expectedQuarters).AsCollection);
			Assert.That(host.ById("historyGraph.chart").Number(UiComponentProperties.PlotTop),
				Is.EqualTo(0.66),
				"the chart keeps to a band at the foot of the card, clear of the value");
		});
	}

	private static UiTestHost Render(object data, string? unit = null)
	{
		var config = Config(data);
		var state = new UiState<HistoryGraphViewState>(Resolver(data, Registry(unit: unit)).Resolve(_nothing));

		return UiTestHost.Render(HistoryGraphWidgetView.Build(state, config),
			new UiSurface { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared });
	}
}
