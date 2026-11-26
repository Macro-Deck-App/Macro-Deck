using System.Globalization;
using MacroDeckHost.Rendering.Dom;
using MacroDeckHost.Rendering.Fit;
using MacroDeckHost.Rendering.Layout;
using MacroDeckHost.Rendering.Text;
using SkiaSharp;

namespace MacroDeckHost.Rendering.Paint;

internal static class TextPainter
{
	private const string Ellipsis = "…";

	public static void Paint(SKCanvas canvas, Element element, SKRect rect, TilePipeline pipeline)
	{
		var style = element.Computed;
		var padding = LayoutEngine.Padding(style, null);
		var contentRight = rect.Right - (float)padding.Right.Value;
		var lines = element.Box.Lines;
		var clamp = style.Display == "-webkit-box" ? style.LineClamp : 0;
		var shown = clamp > 0 ? Math.Min(clamp, lines.Count) : lines.Count;
		var ellipsize = style.Get("text-overflow") == "ellipsis" || clamp > 0 && lines.Count > clamp;
		var shadow = Shadow(style.Get("text-shadow"));
		var outlineColor = CssColor.Parse(element.Attributes.GetValueOrDefault("data-outline-color"));
		var outlineWidth = double.TryParse(element.Attributes.GetValueOrDefault("data-outline-width"),
			NumberStyles.Float,
			CultureInfo.InvariantCulture,
			out var stroke)
			? (float)stroke
			: 0;
		for (var index = 0; index < shown; index++)
		{
			var line = lines[index];
			var last = index == shown - 1;
			var cut = ellipsize && (line.Offset + line.Width).Value > contentRight + 0.01
				|| last && clamp > 0 && lines.Count > clamp;
			var fragments = cut ? Truncate(line, contentRight, pipeline) : line.Fragments;
			var baseline = (float)(line.Top + line.Baseline).Value;
			foreach (var fragment in fragments)
			{
				var x = (float)(line.Offset.Value + fragment.X);
				var color = Painter.Current(fragment.Owner.Computed);
				using var blob = Blob(pipeline.Shaper.Shape(fragment.Text, fragment.Font), fragment.Font);
				if (blob is null)
				{
					continue;
				}

				if (shadow is { } cast && outlineColor is null)
				{
					using var shadowPaint = new SKPaint
					{
						IsAntialias = true,
						Color = cast.Color,
						MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Math.Max(0.01f, cast.Blur / 2))
					};
					canvas.DrawText(blob, x + cast.X, baseline + cast.Y, shadowPaint);
				}

				if (outlineColor is { } outline && outlineWidth > 0)
				{
					using var strokePaint = new SKPaint
					{
						IsAntialias = true,
						Color = outline,
						Style = SKPaintStyle.Stroke,
						StrokeWidth = outlineWidth * 2,
						StrokeJoin = SKStrokeJoin.Round
					};
					canvas.DrawText(blob, x, baseline, strokePaint);
				}

				using var paint = new SKPaint { IsAntialias = true, Color = color };
				canvas.DrawText(blob, x, baseline, paint);
			}
		}
	}

	private static List<TextFragment> Truncate(TextLine line, float right, TilePipeline pipeline)
	{
		var result = new List<TextFragment>();
		if (line.Fragments.Count == 0)
		{
			return result;
		}

		var font = line.Fragments[^1].Font;
		var ellipsisWidth = pipeline.Shaper.Width(Ellipsis, font);
		var limit = right - line.Offset.Value - ellipsisWidth;
		foreach (var fragment in line.Fragments)
		{
			if (fragment.X + fragment.Width <= limit)
			{
				result.Add(fragment);
				continue;
			}

			var text = fragment.Text;
			while (text.Length > 0 && fragment.X + pipeline.Shaper.Width(text, fragment.Font) > limit)
			{
				text = text[..StringInfo.ParseCombiningCharacters(text)[^1]];
			}

			text = text.TrimEnd(' ') + Ellipsis;
			result.Add(fragment with { Text = text });
			return result;
		}

		return result;
	}

	private static SKTextBlob? Blob(ShapedText shaped, FontSpec spec)
	{
		using var builder = new SKTextBlobBuilder();
		double x = 0;
		var any = false;
		foreach (var run in shaped.Runs)
		{
			var buffer = run.Face.AllocateRun(builder, (float)spec.Size, run.Glyphs.Length);
			var glyphs = buffer.Glyphs;
			var positions = buffer.Positions;
			for (var index = 0; index < run.Glyphs.Length; index++)
			{
				glyphs[index] = (ushort)run.Glyphs[index];
				positions[index] = new SKPoint((float)(x + run.OffsetsX[index]), (float)-run.OffsetsY[index]);
				x += run.Advances[index];
			}

			any |= run.Glyphs.Length > 0;
		}

		return any ? builder.Build() : null;
	}

	private sealed record TextShadow(float X, float Y, float Blur, SKColor Color);

	private static TextShadow? Shadow(string value)
	{
		if (value is "" or "none")
		{
			return null;
		}

		var colorStart = value.IndexOf("rgb", StringComparison.Ordinal);
		var lengths = (colorStart >= 0 ? value[..colorStart] : value).Split(' ', StringSplitOptions.RemoveEmptyEntries)
			.Select(part => (float)CssLength.Parse(part).Px)
			.ToList();
		var color = CssColor.Parse(colorStart >= 0 ? value[colorStart..] : null) ?? SKColors.Black;
		return lengths.Count < 2
			? null
			: new TextShadow(lengths[0], lengths[1], lengths.Count > 2 ? lengths[2] : 0, color);
	}
}
