using MacroDeckHost.Rendering.Dom;
using MacroDeckHost.Rendering.Fit;
using SkiaSharp;

namespace MacroDeckHost.Rendering.Paint;

internal static class Rings
{
	public const long PeriodMs = 336_000;

	public const float WidthDevicePx = 2;

	public static readonly IReadOnlyDictionary<string, long> CycleMs =
		new Dictionary<string, long>(StringComparer.Ordinal)
		{
			["heartbeat"] = 1400,
			["breathing"] = 3200,
			["blink"] = 1000,
			["hue-shift"] = 8000,
			["rgb"] = 4000,
			["comet"] = 1600,
			["ants"] = 12000
		};

	public static void Paint(SKCanvas canvas, Element ring, SKRect rect, SKPoint radius, TilePipeline pipeline)
	{
		Paint(canvas,
			ring.Attributes.GetValueOrDefault("data-ring-style") ?? "static",
			CssColor.Parse(ring.Attributes.GetValueOrDefault("data-ring-color")) ?? SKColors.White,
			rect,
			radius,
			WidthDevicePx / (float)pipeline.Input.Scale,
			pipeline.Input.Now);
	}

	public static void PaintTileRing(
		SKCanvas canvas,
		string style,
		SKColor color,
		SKRect rect,
		float radius,
		DateTimeOffset now)
	{
		Paint(canvas, style, color, rect, new SKPoint(radius, radius), WidthDevicePx, now);
	}

	private static void Paint(
		SKCanvas canvas,
		string style,
		SKColor color,
		SKRect rect,
		SKPoint radius,
		float width,
		DateTimeOffset now)
	{
		var nowMs = now.ToUnixTimeMilliseconds() % PeriodMs;
		var phase = CycleMs.TryGetValue(style, out var cycle) ? nowMs % cycle / (double)cycle : 0;
		var inset = rect;
		inset.Inflate(-width / 2, -width / 2);
		var shape = new SKRoundRect(inset, Math.Max(0, radius.X - width / 2), Math.Max(0, radius.Y - width / 2));
		using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width };
		switch (style)
		{
			case "heartbeat":
				paint.Color = color.WithAlpha(Alpha(Keyframes(phase,
					(0, 0.35),
					(0.1, 1),
					(0.2, 0.45),
					(0.3, 1),
					(0.45, 0.35),
					(1, 0.35))));
				break;
			case "breathing":
				paint.Color = color.WithAlpha(Alpha(Keyframes(phase, (0, 0.15), (0.5, 1), (1, 0.15))));
				break;
			case "blink":
				paint.Color = color.WithAlpha(phase < 0.5 ? (byte)255 : (byte)0);
				break;
			case "hue-shift":
				paint.Color = SKColor.FromHsl((float)(phase * 360), 100, 50);
				break;
			case "rgb":
			case "comet":
			case "ants":
				paint.Shader = Spin(style, color, rect, phase);
				break;
			default:
				paint.Color = color;
				break;
		}

		canvas.DrawRoundRect(shape, paint);
	}

	private static byte Alpha(double opacity)
	{
		return (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
	}

	private static double Keyframes(double phase, params (double At, double Value)[] frames)
	{
		for (var index = 1; index < frames.Length; index++)
		{
			if (phase <= frames[index].At)
			{
				var from = frames[index - 1];
				var to = frames[index];
				var t = to.At > from.At ? (phase - from.At) / (to.At - from.At) : 1;
				var eased = t * t * (3 - 2 * t);
				return from.Value + (to.Value - from.Value) * eased;
			}
		}

		return frames[^1].Value;
	}

	private static SKShader Spin(string style, SKColor color, SKRect rect, double phase)
	{
		var rotation = SKMatrix.CreateRotationDegrees((float)(phase * 360 - 90), rect.MidX, rect.MidY);
		var centre = new SKPoint(rect.MidX, rect.MidY);
		return style switch
		{
			"rgb" => SKShader.CreateSweepGradient(centre,
				[
					SKColors.Red, SKColors.Yellow, SKColors.Lime, SKColors.Cyan, SKColors.Blue, SKColors.Magenta,
					SKColors.Red
				],
				null,
				rotation),
			"comet" => SKShader.CreateSweepGradient(centre,
				[SKColors.Transparent, SKColors.Transparent, color],
				[0, 210f / 360, 1],
				rotation),
			_ => SKShader.CreateSweepGradient(centre,
				Enumerable.Range(0, 40)
					.SelectMany(index => index % 2 == 0
						? new[] { color, color }
						: [SKColors.Transparent, SKColors.Transparent])
					.ToArray(),
				Enumerable.Range(0, 40).SelectMany(index => new[] { index / 40f, (index + 1) / 40f }).ToArray(),
				rotation)
		};
	}
}
