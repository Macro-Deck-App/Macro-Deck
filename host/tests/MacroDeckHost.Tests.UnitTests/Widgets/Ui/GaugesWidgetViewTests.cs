using MacroDeck.Ui.Components;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Testing;
using MacroDeckHost.Widgets.Gauges;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

[TestFixture]
public class GaugesWidgetViewTests
{
	[Test]
	public void Four_gauges_on_a_square_tile_sit_in_two_rows_of_two()
	{
		var host = Render(Gauges(4));
		var layout = host.SingleByType(UiComponents.Responsive);
		var square = layout.Children.Single(child => child.Type == UiComponents.Grid && child.Number(UiComponentProperties.Columns) == 2);

		Assert.That(square.Number(UiComponentProperties.Rows), Is.EqualTo(2));
	}

	[Test]
	public void Four_gauges_on_a_wide_tile_sit_in_a_single_row()
	{
		var segments = GaugesLayout.For(4);
		var wide = segments.Single(segment => segment.Contains(2.1));

		Assert.Multiple(() =>
		{
			Assert.That(wide.Columns, Is.EqualTo(4));
			Assert.That(wide.Rows, Is.EqualTo(1));
			Assert.That(segments.Single(segment => segment.Contains(1)).Columns, Is.EqualTo(2));
		});
	}

	[TestCase(1)]
	[TestCase(2)]
	[TestCase(3)]
	[TestCase(5)]
	[TestCase(8)]
	public void Every_aspect_has_exactly_one_layout_and_every_layout_fits_every_gauge(int count)
	{
		var segments = GaugesLayout.For(count);

		Assert.Multiple(() =>
		{
			foreach (var aspect in new[] { 0.2, 0.5, 1, 1.5, 2.1, 3, 5, 8 })
			{
				Assert.That(segments.Count(segment => segment.Contains(aspect)), Is.EqualTo(1), $"aspect {aspect}");
			}

			foreach (var segment in segments)
			{
				Assert.That(segment.Columns * segment.Rows, Is.GreaterThanOrEqualTo(count));
			}
		});
	}

	[Test]
	public void A_single_gauge_needs_no_responsive_layout()
	{
		var host = Render(Gauges(1));

		Assert.That(host.ByType(UiComponents.Responsive), Is.Empty);
		Assert.That(host.ByType(UiComponents.Gauge), Has.Count.EqualTo(1));
	}

	[Test]
	public void Only_the_first_eight_gauges_are_drawn()
	{
		var config = Gauges(10);
		var host = Render(config);
		var drawn = host.SingleByType(UiComponents.Responsive).Children[0];

		Assert.That(drawn.Children, Has.Count.EqualTo(GaugesWidgetData.MaxGauges));
	}

	[Test]
	public void Eight_fully_dressed_gauges_stay_far_inside_the_tree_limits()
	{
		var config = Gauges(8) with { Title = "System" };
		var host = Render(config);

		Assert.That(Count(host.Root), Is.LessThan(1000));
	}

	[Test]
	public void Gauges_keyed_by_long_generated_ids_still_build_a_valid_tree()
	{
		var config = new GaugesWidgetData
		{
			Title = "System",
			Gauges = Enumerable.Range(0, 3)
				.Select(_ => new GaugeConfig { Id = Guid.NewGuid().ToString("N"), Name = "n" })
				.ToList(),
		};

		Assert.That(() => Render(config), Throws.Nothing);
	}

	[Test]
	public void A_resolved_icon_is_drawn_inside_the_ring_and_a_missing_one_draws_nothing()
	{
		var config = new GaugesWidgetData { Gauges = [new GaugeConfig { Id = "a" }, new GaugeConfig { Id = "b" }] };
		var resource = new UiResource { ResourceId = "icon-a" };
		var host = Render(config, new Dictionary<string, UiResource> { ["a"] = resource });

		Assert.Multiple(() =>
		{
			Assert.That(host.ByType(UiComponents.Image), Has.Count.EqualTo(1));
			Assert.That(host.ByType(UiComponents.Button), Is.Empty);
		});
	}

	[Test]
	public void The_icon_colour_recolours_the_icon_on_a_transparent_background_without_taking_the_press()
	{
		var tinted = Render(new GaugesWidgetData
			{
				Gauges = [new GaugeConfig { Id = "a", IconColor = "#34c759" }],
			},
			new Dictionary<string, UiResource> { ["a"] = new() { ResourceId = "icon-a" } });
		var button = tinted.SingleByType(UiComponents.Button);

		Assert.Multiple(() =>
		{
			Assert.That(tinted.ByType(UiComponents.Image), Is.Empty);
			Assert.That(button.Text(UiComponentProperties.Tint), Is.EqualTo("#34c759"));
			Assert.That(button.Text(UiComponentProperties.Background), Is.EqualTo("transparent"),
				"an absent button background is the accent colour");
			Assert.That(button.PropertyKeys, Does.Not.Contain("events"), "the tinted icon must not take the tile's press");
		});
	}

	[Test]
	public void The_ring_style_is_a_full_turn_and_the_arc_style_leaves_the_default_sweep()
	{
		var ring = Render(Gauges(1)).SingleByType(UiComponents.Gauge);
		var arc = Render(Gauges(1) with { Style = GaugesWidgetData.StyleArc }).SingleByType(UiComponents.Gauge);

		Assert.Multiple(() =>
		{
			Assert.That(ring.Number(UiComponentProperties.StartAngle), Is.EqualTo(0));
			Assert.That(ring.Number(UiComponentProperties.EndAngle), Is.EqualTo(360));
			Assert.That(arc.HasProperty(UiComponentProperties.StartAngle), Is.False);
			Assert.That(arc.HasProperty(UiComponentProperties.EndAngle), Is.False);
		});
	}

	[Test]
	public void The_ring_shows_the_face_level_and_colour_and_follows_a_new_reading()
	{
		var config = Gauges(1);
		var face = new UiState<GaugeFace>(new GaugeFace { Level = 0.25, Value = "25" });
		var host = UiTestHost.Render(GaugesWidgetView.Build(config, [face]));

		face.Set(new GaugeFace { Level = 0.9, Value = "90", Color = "#ff3b30" });
		var ring = host.SingleByType(UiComponents.Gauge);

		Assert.Multiple(() =>
		{
			Assert.That(ring.Number(UiComponentProperties.Level), Is.EqualTo(0.9));
			Assert.That(ring.Text(UiComponentProperties.LevelColor), Is.EqualTo("#ff3b30"));
			Assert.That(host.ByText("90"), Is.Not.Empty);
		});
	}

	[Test]
	public void The_name_is_drawn_below_the_value_only_when_set()
	{
		var config = new GaugesWidgetData
		{
			Gauges = [new GaugeConfig { Id = "a", Name = "CPU" }, new GaugeConfig { Id = "b" }],
		};

		var ids = Ids(Render(config).Root).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(ids.Where(id => id.EndsWith(".g0.name", StringComparison.Ordinal)), Is.Not.Empty);
			Assert.That(ids.Where(id => id.EndsWith(".g1.name", StringComparison.Ordinal)), Is.Empty);
		});
	}

	[Test]
	public void A_widget_without_gauges_shows_a_hint_instead_of_an_empty_tile()
	{
		var host = Render(new GaugesWidgetData());

		Assert.Multiple(() =>
		{
			Assert.That(host.ByType(UiComponents.Gauge), Is.Empty);
			Assert.That(host.FindById("gauges.emptyHint.emptyText"), Is.Not.Null);
		});
	}

	private static GaugesWidgetData Gauges(int count)
		=> new()
		{
			Gauges = Enumerable.Range(0, count)
				.Select(index => new GaugeConfig
					{ Id = $"g{index}", Name = $"Gauge {index}", Variable = "v" })
				.ToList(),
		};

	private static UiTestHost Render(GaugesWidgetData config, IReadOnlyDictionary<string, UiResource>? icons = null)
	{
		var faces = config.Shown.Select(gauge => new UiState<GaugeFace>(GaugeFace.Empty with { Name = gauge.Name ?? string.Empty }))
			.ToList();

		return UiTestHost.Render(GaugesWidgetView.Build(config, faces, icons));
	}

	private static IEnumerable<string> Ids(UiTestNode node)
		=> new[] { node.Id }
			.Concat(node.Children.SelectMany(Ids))
			.Concat(node.Fallback is { } fallback ? Ids(fallback) : []);

	private static int Count(UiTestNode node)
		=> 1 + node.Children.Sum(Count) + (node.Fallback is { } fallback ? Count(fallback) : 0);
}
