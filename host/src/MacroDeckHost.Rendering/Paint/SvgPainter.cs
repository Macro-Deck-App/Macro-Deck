using System.Globalization;
using MacroDeckHost.Rendering.Dom;
using SkiaSharp;

namespace MacroDeckHost.Rendering.Paint;

internal static class SvgPainter
{
	public static void Paint(SKCanvas canvas, Element svg)
	{
		foreach (var child in svg.Children)
		{
			var style = child.Computed;
			var fill = Paint(Value(child, "fill"), style, child.Tag == "line" ? "none" : "black");
			var stroke = Paint(Value(child, "stroke"), style, "none");
			var strokeWidth = (float)Number(child, "stroke-width", 1);
			using var path = Geometry(child);
			if (path is null)
			{
				continue;
			}

			var fillOpacity = (float)style.Number("fill-opacity", 1);
			var opacity = (float)style.Number("opacity", 1);
			var round = style.Get("stroke-linecap") == "round";
			if (fill is { } fillColor)
			{
				using var paint = new SKPaint
				{
					IsAntialias = true,
					Color = fillColor.WithAlpha((byte)(fillColor.Alpha * fillOpacity * opacity))
				};
				canvas.DrawPath(path, paint);
			}

			if (stroke is { } strokeColor && strokeWidth > 0)
			{
				using var paint = new SKPaint
				{
					IsAntialias = true,
					Style = SKPaintStyle.Stroke,
					StrokeWidth = strokeWidth,
					Color = strokeColor.WithAlpha((byte)(strokeColor.Alpha * opacity)),
					StrokeCap = round ? SKStrokeCap.Round : SKStrokeCap.Butt,
					StrokeJoin = style.Get("stroke-linejoin") == "round" ? SKStrokeJoin.Round : SKStrokeJoin.Miter
				};
				canvas.DrawPath(path, paint);
			}
		}
	}

	private static string? Value(Element element, string name)
	{
		return element.Style.TryGetValue(name, out var inline) ? element.Computed.Get(name, inline)
			: element.Computed.Has(name) ? element.Computed.Get(name)
			: element.Attributes.GetValueOrDefault(name);
	}

	private static SKColor? Paint(string? value, ComputedStyle style, string fallback)
	{
		var resolved = value ?? fallback;
		if (resolved == "none")
		{
			return null;
		}

		return resolved == "currentColor" ? Painter.Current(style) : CssColor.Parse(resolved);
	}

	private static double Number(Element element, string name, double fallback)
	{
		return double.TryParse(element.Attributes.GetValueOrDefault(name),
			NumberStyles.Float,
			CultureInfo.InvariantCulture,
			out var value)
			? value
			: fallback;
	}

	private static SKPath? Geometry(Element element)
	{
		switch (element.Tag)
		{
			case "path":
			{
				var path = SKPath.ParseSvgPathData(element.Attributes.GetValueOrDefault("d") ?? "");
				if (path is not null
					&& element.Attributes.GetValueOrDefault("transform") is { } transform
					&& transform.StartsWith("scale(", StringComparison.Ordinal))
				{
					var parts = transform[6..^1]
						.Split(' ', StringSplitOptions.RemoveEmptyEntries)
						.Select(part => float.Parse(part, CultureInfo.InvariantCulture))
						.ToArray();
					path.Transform(SKMatrix.CreateScale(parts[0], parts.Length > 1 ? parts[1] : parts[0]));
				}

				return path;
			}

			case "circle":
			{
				var path = new SKPath();
				path.AddCircle((float)Number(element, "cx", 0),
					(float)Number(element, "cy", 0),
					(float)Number(element, "r", 0));
				return path;
			}

			case "line":
			{
				var path = new SKPath();
				path.MoveTo((float)Number(element, "x1", 0), (float)Number(element, "y1", 0));
				path.LineTo((float)Number(element, "x2", 0), (float)Number(element, "y2", 0));
				return path;
			}

			default:
				return null;
		}
	}
}
