using System.Collections.Concurrent;
using System.Globalization;
using MacroDeckHost.Rendering.Dom;
using MacroDeckHost.Rendering.Fit;
using SkiaSharp;
using Svg.Skia;

namespace MacroDeckHost.Rendering.Paint;

internal static class Artwork
{
	private static readonly ConcurrentDictionary<string, SKPicture?> _icons = new(StringComparer.Ordinal);

	public static bool IsSvg(RenderResource resource)
	{
		var data = resource.Data.Span;
		return resource.MediaType == "image/svg+xml"
			|| data.Length > 0 && (data[0] == (byte)'<' || data[..Math.Min(64, data.Length)].IndexOf("<svg"u8) >= 0);
	}

	public static AnimatedImage? Animated(RenderResource resource)
	{
		return ArtworkCache.Get(resource).Raster is { Frames.Count: > 1 } image ? image : null;
	}

	public static bool LoopingSvg(RenderResource resource)
	{
		return ArtworkCache.Get(resource).Motion is { Loops: true };
	}

	public static void Paint(
		SKCanvas canvas,
		Element element,
		SKRect rect,
		RenderResource resource,
		TilePipeline pipeline)
	{
		var fit = element.Computed.Get("object-fit", "contain");
		var tint = CssColor.Parse(element.Attributes.GetValueOrDefault("data-tint"));
		using var filter = Filter(element.Computed.Get("filter"));
		using var layerPaint = new SKPaint { ColorFilter = filter };
		canvas.SaveLayer(rect, layerPaint);
		var decoded = ArtworkCache.Get(resource);
		if (decoded.Picture is { } picture)
		{
			var motion = pipeline.Input.AnimatedImages ? decoded.Motion : null;
			var now = pipeline.Input.Now.ToUnixTimeMilliseconds();
			DrawFitted(canvas,
				picture.CullRect.Size,
				rect,
				fit,
				target =>
				{
					var matrix = Matrix(picture.CullRect, target);
					if (motion?.TryDraw(canvas, now, matrix) != true)
					{
						canvas.DrawPicture(picture, matrix);
					}
				});
		}
		else if (decoded.Raster is { } image)
		{
			var frame = pipeline.Input.AnimatedImages ? image.FrameAt(pipeline.Input.Now.ToUnixTimeMilliseconds()) : 0;
			var info = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
			using var data = SKData.CreateCopy(image.Frames[frame]);
			using var bitmap = SKImage.FromPixels(info, data, image.Width * 4);
			using var paint = new SKPaint { IsAntialias = true };
			DrawFitted(canvas,
				new SKSize(image.Width, image.Height),
				rect,
				fit,
				target => canvas.DrawImage(bitmap,
					target,
					new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
					paint));
		}

		if (tint is { } color)
		{
			using var paint = new SKPaint { Color = color, BlendMode = SKBlendMode.SrcIn };
			canvas.DrawRect(rect, paint);
		}

		canvas.Restore();
	}

	public static void PaintIcon(SKCanvas canvas, string name, SKRect rect, SKColor color)
	{
		var picture = _icons.GetOrAdd(name,
			key =>
			{
				using var stream = typeof(Artwork).Assembly
					.GetManifestResourceStream($"MacroDeckHost.Rendering.Icons.{key}.svg");
				if (stream is null)
				{
					return null;
				}

				var svg = new SKSvg();
				return svg.Load(stream);
			});
		if (picture is null)
		{
			return;
		}

		canvas.SaveLayer(rect, null);
		DrawFitted(canvas,
			picture.CullRect.Size,
			rect,
			"contain",
			target => canvas.DrawPicture(picture, Matrix(picture.CullRect, target)));
		using var paint = new SKPaint { Color = color, BlendMode = SKBlendMode.SrcIn };
		canvas.DrawRect(rect, paint);
		canvas.Restore();
	}

	private static SKMatrix Matrix(SKRect source, SKRect target)
	{
		var scaleX = source.Width > 0 ? target.Width / source.Width : 1;
		var scaleY = source.Height > 0 ? target.Height / source.Height : 1;
		return SKMatrix.CreateScaleTranslation(scaleX,
			scaleY,
			target.Left - source.Left * scaleX,
			target.Top - source.Top * scaleY);
	}

	private static void DrawFitted(SKCanvas canvas, SKSize natural, SKRect box, string fit, Action<SKRect> draw)
	{
		if (natural.Width <= 0 || natural.Height <= 0 || box.Width <= 0 || box.Height <= 0)
		{
			return;
		}

		var scale = fit == "cover"
			? Math.Max(box.Width / natural.Width, box.Height / natural.Height)
			: Math.Min(box.Width / natural.Width, box.Height / natural.Height);
		var width = natural.Width * scale;
		var height = natural.Height * scale;
		var target = SKRect.Create(box.MidX - width / 2, box.MidY - height / 2, width, height);
		canvas.Save();
		canvas.ClipRect(box);
		draw(target);
		canvas.Restore();
	}

	// CSS brightness() and saturate() as one colour matrix, with the filter spec's luma coefficients.
	private static SKColorFilter? Filter(string value)
	{
		return value.Length == 0 ? null : SKColorFilter.CreateColorMatrix(FilterMatrix(value));
	}

	public static float[] FilterMatrix(string value)
	{
		var matrix = Identity();
		foreach (var part in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			var open = part.IndexOf('(', StringComparison.Ordinal);
			if (open < 0)
			{
				continue;
			}

			var amount = float.Parse(part[(open + 1)..^1], CultureInfo.InvariantCulture);
			matrix = part.StartsWith("brightness", StringComparison.Ordinal) ? Multiply(Brightness(amount), matrix)
				: part.StartsWith("saturate", StringComparison.Ordinal) ? Multiply(Saturate(amount), matrix)
				: matrix;
		}

		return matrix;
	}

	private static float[] Identity()
	{
		return [1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0];
	}

	private static float[] Brightness(float amount)
	{
		return [amount, 0, 0, 0, 0, 0, amount, 0, 0, 0, 0, 0, amount, 0, 0, 0, 0, 0, 1, 0];
	}

	private static float[] Saturate(float s)
	{
		return
		[
			0.213f + 0.787f * s, 0.715f - 0.715f * s, 0.072f - 0.072f * s, 0, 0,
			0.213f - 0.213f * s, 0.715f + 0.285f * s, 0.072f - 0.072f * s, 0, 0,
			0.213f - 0.213f * s, 0.715f - 0.715f * s, 0.072f + 0.928f * s, 0, 0,
			0, 0, 0, 1, 0
		];
	}

	private static float[] Multiply(float[] a, float[] b)
	{
		var result = new float[20];
		for (var row = 0; row < 4; row++)
		{
			for (var column = 0; column < 5; column++)
			{
				var sum = column == 4 ? a[row * 5 + 4] : 0;
				for (var k = 0; k < 4; k++)
				{
					sum += a[row * 5 + k] * b[k * 5 + column];
				}

				result[row * 5 + column] = sum;
			}
		}

		return result;
	}
}
