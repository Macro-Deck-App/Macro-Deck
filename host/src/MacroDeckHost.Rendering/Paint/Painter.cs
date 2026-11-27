using System.Globalization;
using MacroDeckHost.Rendering.Dom;
using MacroDeckHost.Rendering.Fit;
using MacroDeckHost.Rendering.Layout;
using SkiaSharp;

namespace MacroDeckHost.Rendering.Paint;

internal sealed class Painter
{
	private readonly TilePipeline _pipeline;

	private readonly SKCanvas _canvas;

	private Painter(TilePipeline pipeline, SKCanvas canvas)
	{
		_pipeline = pipeline;
		_canvas = canvas;
	}

	public static (byte[] Pixels, int Width, int Height) Paint(TilePipeline pipeline)
	{
		var scale = pipeline.Input.Scale;
		var width = Math.Max(1, (int)Math.Round(pipeline.Input.Width * scale));
		var height = Math.Max(1, (int)Math.Round(pipeline.Input.Height * scale));
		var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
		using var surface = SKSurface.Create(info);
		var canvas = surface.Canvas;
		canvas.Clear(SKColors.Transparent);
		canvas.Scale((float)scale);
		new Painter(pipeline, canvas).PaintElement(pipeline.Tile);
		using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
		surface.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0);
		return (bitmap.Bytes, width, height);
	}

	public static SKColor Current(ComputedStyle style)
	{
		return CssColor.Parse(style.Get("color")) ?? SKColors.White;
	}

	private void PaintElement(Element element)
	{
		var box = element.Box;
		if (!box.Displayed || element.IsText)
		{
			return;
		}

		var style = element.Computed;
		_canvas.Save();
		_canvas.Translate((float)box.X.Value, (float)box.Y.Value);
		var transform = Geometry.OwnTransform(element);
		if (!transform.IsIdentity)
		{
			_canvas.Concat(new SKMatrix((float)transform.A,
				(float)transform.C,
				(float)transform.E,
				(float)transform.B,
				(float)transform.D,
				(float)transform.F,
				0,
				0,
				1));
		}

		var rect = new SKRect(0, 0, (float)box.Width.Value, (float)box.Height.Value);
		var radius = Radius(style, rect);
		var opacity = style.Number("opacity", 1);
		var layered = opacity < 1;
		if (layered)
		{
			using var paint = new SKPaint
			{
				Color = SKColors.White.WithAlpha((byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255))
			};
			_canvas.SaveLayer(paint);
		}

		if (!style.IsHidden)
		{
			PaintBox(element, rect, radius);
		}

		var circle = style.Get("clip-path").StartsWith("circle", StringComparison.Ordinal);
		var clips = style.ClipsOverflow || circle;
		if (clips)
		{
			_canvas.Save();
			if (circle)
			{
				using var path = new SKPath();
				path.AddCircle(rect.MidX,
					rect.MidY,
					(float)(Math.Sqrt(rect.Width * rect.Width + rect.Height * rect.Height) / Math.Sqrt(2) / 2));
				_canvas.ClipPath(path, SKClipOperation.Intersect, true);
			}
			else
			{
				_canvas.ClipRoundRect(new SKRoundRect(rect, radius.X, radius.Y), SKClipOperation.Intersect, true);
			}
		}

		if (!style.IsHidden)
		{
			PaintContent(element, rect);
		}

		_canvas.Save();
		_canvas.Translate((float)-box.ScrollLeft, (float)-box.ScrollTop);
		var children = element.Children.Where(child => !child.IsText && child.Computed.Display != "inline").ToList();
		foreach (var child in children.OrderBy(ZIndex))
		{
			PaintElement(child);
		}

		_canvas.Restore();
		if (clips)
		{
			_canvas.Restore();
		}

		if (element.Classes.Contains("widget-button-ring") && !style.IsHidden)
		{
			Rings.Paint(_canvas, element, rect, radius, _pipeline);
		}

		if (layered)
		{
			_canvas.Restore();
		}

		_canvas.Restore();
	}

	private static int ZIndex(Element element)
	{
		return int.TryParse(element.Computed.Get("z-index"),
			NumberStyles.Integer,
			CultureInfo.InvariantCulture,
			out var index)
			? index
			: 0;
	}

	private static SKPoint Radius(ComputedStyle style, SKRect rect)
	{
		var value = style.Get("border-radius");
		if (value.Length == 0)
		{
			return SKPoint.Empty;
		}

		var length = CssLength.Parse(value.Split(' ')[0]);
		if (!length.IsValue)
		{
			return SKPoint.Empty;
		}

		var x = Math.Min((float)length.Resolve(rect.Width, style.FontSize, 0), rect.Width / 2);
		var y = Math.Min((float)length.Resolve(rect.Height, style.FontSize, 0), rect.Height / 2);
		var both = Math.Min(x, y);
		return length.HasPercent ? new SKPoint(x, y) : new SKPoint(both, both);
	}

	private void PaintBox(Element element, SKRect rect, SKPoint radius)
	{
		var style = element.Computed;
		var shape = new SKRoundRect(rect, radius.X, radius.Y);
		if (CssColor.Gradient(style.Get("background")) is { } gradient)
		{
			using var paint = new SKPaint { IsAntialias = true, Shader = CssColor.Shader(gradient, rect) };
			_canvas.DrawRoundRect(shape, paint);
		}
		else if (CssColor.Parse(style.Get("background-color")) is { Alpha: > 0 } color)
		{
			using var paint = new SKPaint { IsAntialias = true, Color = color };
			_canvas.DrawRoundRect(shape, paint);
		}

		var border = LayoutEngine.Border(style);
		if (border.Top.Value > 0)
		{
			var width = (float)border.Top.Value;
			var color = CssColor.Parse(style.Get("border-top-color")) ?? Current(style);
			using var paint = new SKPaint
			{
				IsAntialias = true,
				Style = SKPaintStyle.Stroke,
				StrokeWidth = width,
				Color = color
			};
			Dash(paint, style.Get("border-top-style"), width);
			var inset = rect;
			inset.Inflate(-width / 2, -width / 2);
			_canvas.DrawRoundRect(
				new SKRoundRect(inset, Math.Max(0, radius.X - width / 2), Math.Max(0, radius.Y - width / 2)),
				paint);
		}

		if (style.Get("outline").Length > 0)
		{
			var parts = style.Get("outline").Split(' ', 3);
			var width = (float)CssLength.Parse(parts[0]).Px;
			var offset = (float)CssLength.Parse(style.Get("outline-offset", "0")).Px;
			var color = CssColor.Parse(parts.Length > 2 ? parts[2] : null) ?? Current(style);
			using var paint = new SKPaint
			{
				IsAntialias = true,
				Style = SKPaintStyle.Stroke,
				StrokeWidth = width,
				Color = color
			};
			Dash(paint, parts.Length > 1 ? parts[1] : "solid", width);
			var outer = rect;
			outer.Inflate(offset + width / 2, offset + width / 2);
			_canvas.DrawRoundRect(new SKRoundRect(outer,
					Math.Max(0, radius.X + offset + width / 2),
					Math.Max(0, radius.Y + offset + width / 2)),
				paint);
		}
	}

	private static void Dash(SKPaint paint, string line, float width)
	{
		if (line == "dashed")
		{
			paint.PathEffect = SKPathEffect.CreateDash([width * 3, width * 3], 0);
		}
		else if (line == "dotted")
		{
			paint.StrokeCap = SKStrokeCap.Round;
			paint.PathEffect = SKPathEffect.CreateDash([0.01f, width * 2], 0);
		}
	}

	private void PaintContent(Element element, SKRect rect)
	{
		switch (element.Tag)
		{
			case "svg":
				SvgPainter.Paint(_canvas, element);
				return;
			case "img" when element.Paint is RenderResource resource:
				Artwork.Paint(_canvas, element, rect, resource, _pipeline);
				return;
		}

		if (element.Paint is string icon && element.Classes.Contains("widget-icon-glyph"))
		{
			Artwork.PaintIcon(_canvas, icon, rect, Current(element.Computed));
			return;
		}

		if (element.Box.Lines.Count > 0)
		{
			TextPainter.Paint(_canvas, element, rect, _pipeline);
		}
	}
}
