using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using MacroDeckHost.Rendering;
using MacroDeckHost.Rendering.Animation;
using MacroDeckHost.Rendering.Dom;
using MacroDeckHost.Rendering.Paint;

namespace MacroDeckHost.Tests.Rendering;

public sealed class KeyCellComposerTests
{
	private const int Key = 72;

	private static readonly (byte R, byte G, byte B) _bgPrimary = (0x12, 0x12, 0x12);

	private static readonly (byte R, byte G, byte B) _bgSecondary = (0x1a, 0x1a, 0x1a);

	private static JsonElement Json(string json)
	{
		using var document = JsonDocument.Parse(json);
		return document.RootElement.Clone();
	}

	private static JsonElement Tree(string root)
	{
		return Json($$"""{"revision":1,"root":{{root}}}""");
	}

	private static KeyCellInput Input(KeyCellKind kind)
	{
		return new KeyCellInput
		{
			KeyWidth = Key,
			KeyHeight = Key,
			Kind = kind,
			Theme = RenderTheme.Dark,
			Now = Fixtures.FixedNow,
			Fonts = Fixtures.Fonts,
			Localization = Fixtures.Catalog
		};
	}

	private static (byte R, byte G, byte B) Pixel(KeyPicture picture, int x, int y)
	{
		var index = (y * picture.Width + x) * 4;
		return (picture.Pixels[index], picture.Pixels[index + 1], picture.Pixels[index + 2]);
	}

	[Test]
	public async Task An_empty_cell_is_the_secondary_background_at_a_third_over_the_deck_and_rounded()
	{
		var output = KeyCellComposer.Compose(Input(KeyCellKind.Empty) with { CornerRadius = 22 });

		var key = output.Keys.Single();
		var tinted = (byte)Math.Round(0x12 + (0x1a - 0x12) * 0.35);
		await Assert.MultipleAsync(async () =>
		{
			Assert.That((key.Width, key.Height), Is.EqualTo((Key, Key)));
			Assert.That(Pixel(key, 36, 36).R, Is.EqualTo(tinted).Within(1));
			Assert.That(Pixel(key, 0, 0), Is.EqualTo(_bgPrimary), "the corner is outside the radius");
			Assert.That(output.NextChangeAt, Is.Null);
			await Task.CompletedTask;
		});
	}

	[Test]
	public async Task A_cell_the_folder_does_not_reach_is_the_folder_background_only()
	{
		var output = KeyCellComposer.Compose(Input(KeyCellKind.Background) with { Background = "#204060" });

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(Pixel(output.Keys.Single(), 36, 36), Is.EqualTo(((byte)0x20, (byte)0x40, (byte)0x60)));
			Assert.That(Pixel(output.Keys.Single(), 0, 0), Is.EqualTo(((byte)0x20, (byte)0x40, (byte)0x60)));
			await Task.CompletedTask;
		});
	}

	[Test]
	public async Task A_transparent_tile_shows_the_folder_background_through_its_face()
	{
		var transparent = KeyCellComposer.Compose(Input(KeyCellKind.Tile) with
		{
			Background = "#204060",
			WidgetType = "Clock",
			Tree = Tree("""{"id":"r","type":"ui.stack","properties":{"background":"transparent"}}""")
		});
		var opaqueFace = KeyCellComposer.Compose(Input(KeyCellKind.Tile) with
		{
			Background = "#204060",
			WidgetType = "Clock",
			Tree = Tree("""{"id":"r","type":"ui.stack"}""")
		});

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(Pixel(transparent.Keys.Single(), 36, 36), Is.EqualTo(((byte)0x20, (byte)0x40, (byte)0x60)));
			Assert.That(Pixel(opaqueFace.Keys.Single(), 36, 36), Is.EqualTo(_bgSecondary));
			await Task.CompletedTask;
		});
	}

	[Test]
	public async Task A_static_border_from_widget_data_is_a_ring_two_device_pixels_wide_at_the_key_edge()
	{
		var output = KeyCellComposer.Compose(Input(KeyCellKind.Tile) with
		{
			WidgetType = "Clock",
			WidgetData = Json("""{"border":{"style":"static","color":"#00ff00"}}"""),
			Tree = Tree("""{"id":"r","type":"ui.stack"}""")
		});

		var key = output.Keys.Single();
		await Assert.MultipleAsync(async () =>
		{
			Assert.That(Pixel(key, 0, 36), Is.EqualTo(((byte)0, (byte)255, (byte)0)));
			Assert.That(Pixel(key, 1, 36), Is.EqualTo(((byte)0, (byte)255, (byte)0)));
			Assert.That(Pixel(key, 3, 36), Is.EqualTo(_bgSecondary));
			Assert.That(output.NextChangeAt, Is.Null, "a static ring never changes");
			await Task.CompletedTask;
		});
	}

	[Test]
	public async Task An_action_button_takes_its_border_from_the_root_button_and_not_from_its_data()
	{
		var root = """
				   {"id":"r","type":"ui.button",
				   	"properties":{"background":"#000080","borderStyle":"static","borderColor":"#ff0000"}}
				   """;
		var output = KeyCellComposer.Compose(Input(KeyCellKind.Tile) with
		{
			WidgetType = "ActionButton",
			WidgetData = Json("""{"border":{"style":"static","color":"#00ff00"}}"""),
			Tree = Tree(root)
		});
		var noRing = KeyCellComposer.Compose(Input(KeyCellKind.Tile) with
		{
			WidgetType = "ActionButton",
			WidgetData = Json("""{"border":{"style":"static","color":"#00ff00"}}"""),
			Tree = Tree("""{"id":"r","type":"ui.button","properties":{"background":"#000080"}}""")
		});
		var bordersOff = KeyCellComposer.Compose(Input(KeyCellKind.Tile) with
		{
			WidgetType = "ActionButton",
			Borders = false,
			Tree = Tree(root)
		});

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(Pixel(output.Keys.Single(), 0, 36), Is.EqualTo(((byte)255, (byte)0, (byte)0)));
			Assert.That(Pixel(noRing.Keys.Single(), 0, 36), Is.Not.EqualTo(((byte)0, (byte)255, (byte)0)));
			Assert.That(Pixel(bordersOff.Keys.Single(), 0, 36), Is.Not.EqualTo(((byte)255, (byte)0, (byte)0)));
			await Task.CompletedTask;
		});
	}

	[Test]
	public async Task An_animated_ring_asks_for_its_next_frame()
	{
		var output = KeyCellComposer.Compose(Input(KeyCellKind.Tile) with
		{
			WidgetType = "Clock",
			WidgetData = Json("""{"border":{"style":"breathing","color":"#00ff00"}}""")
		});

		Assert.That(output.NextChangeAt, Is.GreaterThan(Fixtures.FixedNow));
		await Task.CompletedTask;
	}

	[TestCase(0)]
	[TestCase(12)]
	public async Task A_tile_spanning_two_keys_is_drawn_once_and_sliced_without_the_gap(double spacing)
	{
		var tree = Tree("""
						{"id":"r","type":"ui.stack","properties":{"direction":"horizontal","background":"#000000"},"children":[
							{"id":"a","type":"ui.stack","properties":{"fill":true,"background":"#ff0000"}},
							{"id":"b","type":"ui.stack","properties":{"fill":true,"background":"#0000ff"}}]}
						""");

		var output = KeyCellComposer.Compose(Input(KeyCellKind.Tile) with
		{
			Columns = 2,
			Spacing = spacing,
			WidgetType = "Clock",
			WidgetData = Json("""{"border":{"style":"static","color":"#00ff00"}}"""),
			Tree = tree
		});

		var left = output.Keys.Single(key => key is { Row: 0, Column: 0 });
		var right = output.Keys.Single(key => key is { Row: 0, Column: 1 });
		var green = ((byte)0, (byte)255, (byte)0);
		await Assert.MultipleAsync(async () =>
		{
			Assert.That(output.Keys, Has.Count.EqualTo(2));
			Assert.That((right.Width, right.Height), Is.EqualTo((Key, Key)));
			Assert.That(Pixel(left, 0, 36), Is.EqualTo(green), "the ring runs along the tile's outer edge");
			Assert.That(Pixel(left, Key - 1, 36), Is.EqualTo(((byte)255, (byte)0, (byte)0)));
			Assert.That(Pixel(right, 0, 36), Is.EqualTo(((byte)0, (byte)0, (byte)255)));
			Assert.That(Pixel(right, Key - 1, 36), Is.EqualTo(green));
			await Task.CompletedTask;
		});
	}

	[Test]
	public async Task A_rotated_key_is_rendered_at_the_swapped_size_and_delivered_at_the_key_size()
	{
		var output = KeyCellComposer.Compose(Input(KeyCellKind.Empty) with { KeyWidth = 60, KeyHeight = 80 });
		var key = output.Keys.Single();

		var encoded = KeyImageEncoder.Encode(key.Pixels, key.Width, key.Height, KeyImageFormat.Rgba, false, 90);

		await Assert.MultipleAsync(async () =>
		{
			Assert.That((key.Width, key.Height), Is.EqualTo((60, 80)));
			Assert.That((encoded.Width, encoded.Height), Is.EqualTo((80, 60)));
			await Task.CompletedTask;
		});
	}

	[Test]
	public async Task A_pressed_tile_is_tinted_and_drawn_smaller_so_the_deck_shows_at_its_edge()
	{
		var input = Input(KeyCellKind.Tile) with
		{
			WidgetType = "Clock",
			Tree = Tree("""{"id":"r","type":"ui.stack"}""")
		};

		var resting = KeyCellComposer.Compose(input).Keys.Single();
		var pressed = KeyCellComposer.Compose(input with { Pressed = true }).Keys.Single();

		var tinted = (byte)Math.Round(0x1a + (255 - 0x1a) * Motion.PressTintAlpha);
		await Assert.MultipleAsync(async () =>
		{
			Assert.That(Pixel(resting, 36, 36), Is.EqualTo(_bgSecondary));
			Assert.That(Pixel(pressed, 36, 36).R, Is.EqualTo(tinted).Within(1));
			Assert.That(Pixel(resting, 0, 36), Is.EqualTo(_bgSecondary));
			Assert.That(Pixel(pressed, 0, 36), Is.EqualTo(_bgPrimary), "the face moved in from the key edge");
			await Task.CompletedTask;
		});
	}

	[Test]
	public async Task A_pressed_tile_spanning_two_keys_shrinks_as_one_widget()
	{
		var output = KeyCellComposer.Compose(Input(KeyCellKind.Tile) with
		{
			Columns = 2,
			Pressed = true,
			WidgetType = "Clock",
			Tree = Tree("""{"id":"r","type":"ui.stack"}""")
		});

		var left = output.Keys.Single(key => key is { Row: 0, Column: 0 });
		var right = output.Keys.Single(key => key is { Row: 0, Column: 1 });
		await Assert.MultipleAsync(async () =>
		{
			Assert.That(Pixel(left, 0, 36), Is.EqualTo(_bgPrimary));
			Assert.That(Pixel(left, Key - 1, 36), Is.Not.EqualTo(_bgPrimary), "no gap at the inner edge");
			Assert.That(Pixel(right, 0, 36), Is.Not.EqualTo(_bgPrimary));
			Assert.That(Pixel(right, Key - 1, 36), Is.EqualTo(_bgPrimary));
			await Task.CompletedTask;
		});
	}

	[Test]
	public async Task A_pressed_tile_is_resampled_smoothly_rather_than_by_dropping_pixels()
	{
		var tree = Tree("""
						{"id":"r","type":"ui.stack","properties":{"direction":"horizontal"},"children":[
							{"id":"a","type":"ui.stack","properties":{"fill":true,"background":"#ff0000"}},
							{"id":"b","type":"ui.stack","properties":{"fill":true,"background":"#0000ff"}},
							{"id":"c","type":"ui.stack","properties":{"fill":true,"background":"#0000ff"}}]}
						""");

		var pressed = KeyCellComposer.Compose(Input(KeyCellKind.Tile) with
			{
				Pressed = true,
				WidgetType = "Clock",
				Tree = tree
			})
			.Keys.Single();

		var (red, _, blue) = Pixel(pressed, 24, 36);
		await Assert.MultipleAsync(async () =>
		{
			Assert.That(red, Is.InRange(0x60, 0xf0), "the scaled edge falls inside this pixel and blends");
			Assert.That(blue, Is.InRange(0x60, 0xf0));
			await Task.CompletedTask;
		});
	}

	[Test]
	public async Task The_press_tint_is_the_shared_stylesheet_s_active_tint()
	{
		var button = new Element("div");
		button.SetClassName("widget-button");
		var tint = new Element("div");
		tint.SetClassName("widget-press-tint widget-press-tint-active");
		button.Append(tint);
		var rule = StyleSheet.Shared.Rules.Last(item => item.Selector.Matches(tint)
			&& item.Declarations.Any(declaration => declaration.Name == "background-color"));
		var colour = CssColor.Parse(rule.Declarations.Last(declaration => declaration.Name == "background-color")
			.Value);

		Assert.That(colour!.Value.Alpha / 255.0, Is.EqualTo(Motion.PressTintAlpha).Within(0.5 / 255));
		await Task.CompletedTask;
	}

	[Test]
	public async Task The_press_scale_is_the_web_tile_s_pressed_scale()
	{
		var css = await File.ReadAllTextAsync(Path.Combine(Fixtures.RepoRoot, "ui", "runtime", "styles", "grid.css"));
		var match = Regex.Match(css, @"\.deck-grid-tile-pressed\s*\{\s*transform:\s*scale\(([0-9.]+)\)");

		Assert.That(match.Success, Is.True);
		Assert.That(double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), Is.EqualTo(Motion.PressScale));
	}
}
