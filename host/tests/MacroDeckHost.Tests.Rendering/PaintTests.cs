using System.Text.Json;
using MacroDeckHost.Rendering;
using MacroDeckHost.Rendering.Text;
using SkiaSharp;

namespace MacroDeckHost.Tests.Rendering;

public sealed class PaintTests
{
	private static JsonElement Tree(string root)
	{
		using var document = JsonDocument.Parse($$"""{"revision":1,"root":{{root}}}""");
		return document.RootElement.Clone();
	}

	private static (byte R, byte G, byte B, byte A) Pixel(TileRenderOutput output, int x, int y)
	{
		var index = (y * output.PixelWidth + x) * 4;
		return (output.Pixels[index], output.Pixels[index + 1], output.Pixels[index + 2], output.Pixels[index + 3]);
	}

	[Test]
	public async Task An_empty_tile_shows_the_theme_s_secondary_background()
	{
		var renderer = new TileRenderer(Fixtures.Fonts);

		var dark = renderer.Render(Fixtures.Input(Tree("""{"id":"r","type":"ui.stack"}"""), 120, 120, 120));
		var light = renderer.Render(Fixtures.Input(Tree("""{"id":"r","type":"ui.stack"}"""), 120, 120, 120) with
		{
			Theme = RenderTheme.Light
		});

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(Pixel(dark, 60, 60),
				Is.EqualTo(((byte)0x1a, (byte)0x1a, (byte)0x1a, (byte)255)),
				"--color-bg-secondary, dark");
			Assert.That(Pixel(light, 60, 60),
				Is.EqualTo(((byte)0xf5, (byte)0xf5, (byte)0xf5, (byte)255)),
				"--color-bg-secondary, light");
			await Task.CompletedTask;
		});
	}

	[Test]
	public async Task A_stack_background_fills_its_box_at_the_device_scale()
	{
		var input = Fixtures.Input(Tree("""{"id":"r","type":"ui.stack","properties":{"background":"#ff0000"}}"""),
				120,
				120,
				120)
			with
			{
				Scale = 2
			};

		var output = new TileRenderer(Fixtures.Fonts).Render(input);

		await Assert.MultipleAsync(async () =>
		{
			Assert.That((output.PixelWidth, output.PixelHeight), Is.EqualTo((240, 240)));
			Assert.That(Pixel(output, 120, 120), Is.EqualTo(((byte)255, (byte)0, (byte)0, (byte)255)));
			Assert.That(Pixel(output, 1, 238), Is.EqualTo(((byte)255, (byte)0, (byte)0, (byte)255)));
			await Task.CompletedTask;
		});
	}

	[Test]
	public void A_disabled_region_is_drawn_at_the_dim_opacity()
	{
		var tree = Tree("""
						{"id":"r","type":"ui.stack","properties":{"background":"#ffffff","modifiers":{"disabled":true}}}
						""");

		var output = new TileRenderer(Fixtures.Fonts).Render(Fixtures.Input(tree, 120, 120, 120));

		var expected = 0.4 * 255 + 0.6 * 0x1a;
		Assert.That(Pixel(output, 60, 60).R, Is.EqualTo(expected).Within(1.5));
	}

	[Test]
	public async Task A_ticking_clock_asks_for_the_next_second_and_a_static_tree_for_nothing()
	{
		var clock = Tree("""
						 {"id":"c","type":"macrodeck.dynamic-text",
						 "properties":{"format":"time","seconds":true,"size":{"basis":0.2},"value":{"$time":{}}}}
						 """);
		var plain = Tree("""{"id":"t","type":"ui.text","properties":{"size":{"basis":0.2},"text":"Hi"}}""");
		var renderer = new TileRenderer(Fixtures.Fonts);
		var now = new DateTimeOffset(2026, 9, 12, 14, 5, 30, 250, TimeSpan.Zero);

		var ticking = renderer.Render(Fixtures.Input(clock, 120, 120, 120) with { Now = now });
		var still = renderer.Render(Fixtures.Input(plain, 120, 120, 120) with { Now = now });

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(ticking.NextChangeAt, Is.EqualTo(new DateTimeOffset(2026, 9, 12, 14, 5, 31, TimeSpan.Zero)));
			Assert.That(still.NextChangeAt, Is.Null);
			await Task.CompletedTask;
		});
	}

	[Test]
	public void An_image_the_host_has_not_fetched_is_reported_missing()
	{
		var tree = Tree("""
						{"id":"i","type":"ui.image","properties":{"size":{"basis":0.5},"source":{"resourceId":"app.example.cover"}}}
						""");

		var output = new TileRenderer(Fixtures.Fonts).Render(Fixtures.Input(tree, 120, 120, 120));

		Assert.That(output.MissingResources, Is.EqualTo(new[] { "app.example.cover" }));
	}

	[Test]
	public void A_cjk_label_is_set_without_missing_glyphs()
	{
		const string label = "音量調整";
		using var probe = SKFontManager.Default.MatchCharacter(label[0]);
		if (probe is null)
		{
			Assert.Ignore("this machine has no face covering CJK at all");
		}

		var shaper = new TextShaper(Fixtures.Fonts, "ja-JP");

		var shaped = shaper.Shape(label, new FontSpec("sans", 24, false));

		Assert.That(shaped.Runs.SelectMany(run => run.Glyphs),
			Has.None.EqualTo(0u),
			"every character found a covering face");
	}
}
