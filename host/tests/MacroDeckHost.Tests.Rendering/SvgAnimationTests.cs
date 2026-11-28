using System.Text;
using System.Text.Json;
using MacroDeckHost.Rendering;
using MacroDeckHost.Rendering.Paint;
using SkiaSharp;

namespace MacroDeckHost.Tests.Rendering;

public sealed class SvgAnimationTests
{
	private const int Zoom = 10;

	private static readonly long _origin = Fixtures.FixedNow.ToUnixTimeMilliseconds();

	private static readonly (double X, double Height, double Duration, double Begin)[] _bars =
	[
		(5.28, 7.66, 1560, -350),
		(12.0, 13.20, 2040, -700),
		(18.72, 9.77, 1320, -150)
	];

	private static byte[] Icon(params string[] path)
	{
		return File.ReadAllBytes(Path.Combine([Fixtures.RepoRoot, "host", "src", .. path]));
	}

	private static byte[] Playing => Icon("MacroDeckHost.Widgets", "MusicPlayer", "Icons", "playing.svg");

	private static byte[] DiscSpinning => Icon("MacroDeckHost.Widgets", "MusicPlayer", "Icons", "disc-spinning.svg");

	private static RenderResource Resource(byte[] bytes)
	{
		return new RenderResource(bytes, "image/svg+xml", Guid.NewGuid().ToString("N"));
	}

	private static RenderResource Inline(string animation)
	{
		return Resource(Encoding.UTF8.GetBytes($"""
												<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
												<rect width="10" height="10" fill="#ffffff">{animation}</rect></svg>
												"""));
	}

	private static JsonElement ImageTree()
	{
		using var document = JsonDocument.Parse("""
												{"revision":1,"root":{"id":"i","type":"ui.image",
												"properties":{"size":{"basis":1},
												"source":{"resourceId":"app.example.mark"}}}}
												""");
		return document.RootElement.Clone();
	}

	private static TileRenderOutput Render(RenderResource resource, long offsetMs, bool animated)
	{
		return new TileRenderer(Fixtures.Fonts).Render(Fixtures.Input(ImageTree(), 120, 120, 120) with
		{
			Resources = new OneResource(resource),
			AnimatedImages = animated,
			Now = Fixtures.FixedNow.AddMilliseconds(offsetMs)
		});
	}

	private static double CubicBezier(double x1, double y1, double x2, double y2, double x)
	{
		double Point(double a, double b, double t)
		{
			return 3 * a * t * (1 - t) * (1 - t) + 3 * b * t * t * (1 - t) + t * t * t;
		}

		double low = 0;
		double high = 1;
		for (var step = 0; step < 60; step++)
		{
			var middle = (low + high) / 2;
			if (Point(x1, x2, middle) < x)
			{
				low = middle;
			}
			else
			{
				high = middle;
			}
		}

		return Point(y1, y2, (low + high) / 2);
	}

	private static double ExpectedHeight((double X, double Height, double Duration, double Begin) bar, long timeMs)
	{
		var progress = (timeMs - bar.Begin) % bar.Duration / bar.Duration;
		var scale = progress < 0.5
			? 0.5 + 0.5 * CubicBezier(0.42, 0, 0.58, 1, progress / 0.5)
			: 1 - 0.5 * CubicBezier(0.42, 0, 0.58, 1, (progress - 0.5) / 0.5);
		return bar.Height * scale * Zoom;
	}

	private static int[] MeasuredHeights(SvgMotion motion, long timeMs)
	{
		using var bitmap = new SKBitmap(24 * Zoom, 24 * Zoom);
		using var canvas = new SKCanvas(bitmap);
		canvas.Clear(SKColors.Transparent);
		Assert.That(motion.TryDraw(canvas, timeMs, SKMatrix.CreateScale(Zoom, Zoom)), Is.True);
		return _bars.Select(bar => Enumerable.Range(0, bitmap.Height)
				.Count(y => bitmap.GetPixel((int)(bar.X * Zoom), y) is { Red: > 200, Green: > 200, Alpha: > 200 }))
			.ToArray();
	}

	[Test]
	public async Task The_equaliser_bars_follow_their_begin_offsets_key_times_and_splines()
	{
		var motion = SvgMotion.Create(Playing);
		Assert.That(motion, Is.Not.Null);

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(motion!.Loops, Is.True);
			foreach (var offset in new long[] { 0, 100, 250, 400, 780, 1000, 1330, 1900 })
			{
				var time = _origin + offset;
				var measured = MeasuredHeights(motion, time);
				for (var index = 0; index < _bars.Length; index++)
				{
					Assert.That(measured[index],
						Is.EqualTo(ExpectedHeight(_bars[index], time)).Within(1.5),
						$"bar {index + 1} at +{offset} ms");
				}
			}

			await Task.CompletedTask;
		});
	}

	[Test]
	public void A_bar_is_back_at_the_same_height_one_period_later()
	{
		var motion = SvgMotion.Create(Playing)!;

		var first = MeasuredHeights(motion, _origin + 200)[0];
		var period = MeasuredHeights(motion, _origin + 200 + 1560)[0];

		Assert.That(period, Is.EqualTo(first));
	}

	[Test]
	public async Task The_playing_mark_moves_between_renders_and_asks_for_the_next_tenth_of_a_second()
	{
		var resource = Resource(Playing);

		var first = Render(resource, 0, true);
		var later = Render(resource, 250, true);

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(later.Pixels, Is.Not.EqualTo(first.Pixels));
			Assert.That(first.NextChangeAt, Is.EqualTo(Fixtures.FixedNow.AddMilliseconds(100)));
			Assert.That(later.NextChangeAt, Is.EqualTo(Fixtures.FixedNow.AddMilliseconds(300)));
			Assert.That(first.MissingResources, Is.Empty);
			await Task.CompletedTask;
		});
	}

	[Test]
	public async Task The_spinning_disc_turns_once_every_six_seconds()
	{
		var resource = Resource(DiscSpinning);

		var start = Render(resource, 0, true);
		var quarter = Render(resource, 1500, true);
		var turn = Render(resource, 6000, true);

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(quarter.Pixels, Is.Not.EqualTo(start.Pixels));
			Assert.That(turn.Pixels, Is.EqualTo(start.Pixels));
			await Task.CompletedTask;
		});
	}

	[Test]
	public async Task Without_animated_images_the_mark_stands_still_and_asks_for_nothing()
	{
		var resource = Resource(Playing);

		var first = Render(resource, 0, false);
		var later = Render(resource, 250, false);

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(later.Pixels, Is.EqualTo(first.Pixels));
			Assert.That(first.NextChangeAt, Is.Null);
			Assert.That(later.NextChangeAt, Is.Null);
			await Task.CompletedTask;
		});
	}

	[TestCase("MacroDeckHost.Widgets", "MusicPlayer", "Icons", "disc.svg")]
	[TestCase("MacroDeckHost.Integrations", "Weather", "Icons", "animated", "clear-day.svg")]
	public async Task An_svg_without_smil_draws_as_before_with_animated_images_on(params string[] path)
	{
		var resource = Resource(Icon(path));

		var still = Render(resource, 0, false);
		var animated = Render(resource, 0, true);
		var later = Render(resource, 1500, true);

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(animated.Pixels, Is.EqualTo(still.Pixels));
			Assert.That(later.Pixels, Is.EqualTo(still.Pixels));
			Assert.That(animated.NextChangeAt, Is.Null);
			await Task.CompletedTask;
		});
	}

	[TestCase("""<animate attributeName="opacity" values="1;0;1" dur="1s" begin="click" repeatCount="indefinite"/>""")]
	[TestCase("""<animate attributeName="opacity" values="1;0;1" dur="1s" begin="indefinite"/>""")]
	[TestCase("""<animate attributeName="opacity" values="1;0" dur="2s" begin="1s" fill="freeze"/>""")]
	[TestCase("""<animate attributeName="opacity" values="1;0;1" dur="1s" repeatCount="3"/>""")]
	[TestCase("""<animate attributeName="opacity" values="1;0;1" dur="1s" repeatCount="indefinite" end="10s"/>""")]
	public void An_animation_that_never_starts_or_has_settled_asks_for_no_frames(string animation)
	{
		var output = Render(Inline(animation), 0, true);

		Assert.That(output.NextChangeAt, Is.Null);
	}

	[TestCase("""<animate attributeName="opacity" values="1;0;1" dur="1s" repeatDur="indefinite"/>""")]
	[TestCase("""<animate attributeName="opacity" values="1;0;1" dur="1s" repeatCount="indefinite" begin="-0.5s"/>""")]
	public void An_animation_that_runs_on_asks_for_frames(string animation)
	{
		var output = Render(Inline(animation), 0, true);

		Assert.That(output.NextChangeAt, Is.EqualTo(Fixtures.FixedNow.AddMilliseconds(100)));
	}

	[TestCase("""
			  <animate id="a" attributeName="opacity" values="1;0" dur="0.5s" begin="0s;b.end"/>
			  <animate id="b" attributeName="opacity" values="0;1" dur="0.5s" begin="a.end"/>
			  """)]
	[TestCase("""<animate id="a" attributeName="opacity" values="1;0;1" dur="0.1s" begin="0s;a.end"/>""")]
	[TestCase("""<animate attributeName="opacity" values="1;0;1" dur="1s" begin="wallclock(2026-01-01T00:00:00Z)"/>""")]
	public async Task A_chain_of_animations_draws_the_still_picture_and_asks_for_no_frames(string animation)
	{
		var resource = Inline(animation);

		var still = Render(resource, 0, false);
		var animated = Render(resource, 0, true);
		var later = Render(resource, 250, true);

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(animated.Pixels, Is.EqualTo(still.Pixels));
			Assert.That(later.Pixels, Is.EqualTo(still.Pixels));
			Assert.That(animated.NextChangeAt, Is.Null);
			await Task.CompletedTask;
		});
	}

	[Test]
	public void An_animated_svg_drawn_again_and_again_is_decoded_once()
	{
		var resource = Resource(Playing);
		var key = ArtworkCache.KeyOf(resource);
		var decodes = 0;

		void Count(string decoded)
		{
			if (decoded == key)
			{
				Interlocked.Increment(ref decodes);
			}
		}

		ArtworkCache.Decoded += Count;
		try
		{
			for (var frame = 0; frame < 12; frame++)
			{
				Render(resource, frame * 100, true);
			}
		}
		finally
		{
			ArtworkCache.Decoded -= Count;
		}

		Assert.That(decodes, Is.EqualTo(1));
	}

	[Test]
	public void Keys_drawing_one_animated_svg_at_once_get_the_frame_for_their_own_time()
	{
		var resource = Resource(Playing);
		var offsets = Enumerable.Range(0, 16).Select(index => index * 70L).ToArray();
		var expected = offsets.Select(offset => Render(Resource(Playing), offset, true).Pixels).ToArray();

		var actual = new byte[offsets.Length][];
		Parallel.For(0,
			offsets.Length * 4,
			index => actual[index % offsets.Length] = Render(resource, offsets[index % offsets.Length], true).Pixels);

		Assert.That(actual, Is.EqualTo(expected));
	}

	private sealed class OneResource : IRenderResources
	{
		private readonly RenderResource _resource;

		public OneResource(RenderResource resource)
		{
			_resource = resource;
		}

		public RenderResource? TryGet(string resourceId, int? displayPx)
		{
			return _resource;
		}
	}
}
