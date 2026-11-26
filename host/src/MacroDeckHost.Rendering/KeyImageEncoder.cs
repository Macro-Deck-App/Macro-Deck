using System.Buffers.Binary;
using SkiaSharp;

namespace MacroDeckHost.Rendering;

public enum KeyImageFormat
{
	Rgba,
	Png,
	Jpeg,
	Bmp
}

public sealed record EncodedKeyImage(byte[] Data, string ContentType, int Width, int Height);

public static class KeyImageEncoder
{
	public const string RgbaContentType = "application/x-macrodeck-rgba";

	private const int JpegQuality = 90;

	public static EncodedKeyImage Encode(
		byte[] rgba,
		int width,
		int height,
		KeyImageFormat format,
		bool mirrorHorizontal = false,
		int rotation = 0)
	{
		ArgumentNullException.ThrowIfNull(rgba);
		if (rgba.Length != width * height * 4)
		{
			throw new ArgumentException("the buffer does not hold width * height RGBA pixels", nameof(rgba));
		}

		if (rotation is not (0 or 90 or 180 or 270))
		{
			throw new ArgumentOutOfRangeException(nameof(rotation), rotation, "rotation is 0, 90, 180 or 270");
		}

		var (pixels, outWidth, outHeight) = Transform(Opaque(rgba), width, height, mirrorHorizontal, rotation);
		return format switch
		{
			KeyImageFormat.Rgba => new EncodedKeyImage(pixels, RgbaContentType, outWidth, outHeight),
			KeyImageFormat.Png => new EncodedKeyImage(Skia(pixels, outWidth, outHeight, SKEncodedImageFormat.Png, 100),
				"image/png",
				outWidth,
				outHeight),
			KeyImageFormat.Jpeg => new EncodedKeyImage(
				Skia(pixels, outWidth, outHeight, SKEncodedImageFormat.Jpeg, JpegQuality),
				"image/jpeg",
				outWidth,
				outHeight),
			KeyImageFormat.Bmp => new EncodedKeyImage(Bmp(pixels, outWidth, outHeight),
				"image/bmp",
				outWidth,
				outHeight),
			_ => throw new ArgumentOutOfRangeException(nameof(format), format, "unknown key image format")
		};
	}

	private static byte[] Opaque(byte[] rgba)
	{
		var result = new byte[rgba.Length];
		for (var index = 0; index < rgba.Length; index += 4)
		{
			var alpha = rgba[index + 3];
			result[index] = (byte)((rgba[index] * alpha + 127) / 255);
			result[index + 1] = (byte)((rgba[index + 1] * alpha + 127) / 255);
			result[index + 2] = (byte)((rgba[index + 2] * alpha + 127) / 255);
			result[index + 3] = 255;
		}

		return result;
	}

	private static (byte[] Pixels, int Width, int Height) Transform(
		byte[] source,
		int width,
		int height,
		bool mirror,
		int rotation)
	{
		var turned = rotation is 90 or 270;
		var outWidth = turned ? height : width;
		var outHeight = turned ? width : height;
		var result = new byte[source.Length];
		for (var y = 0; y < height; y++)
		{
			for (var x = 0; x < width; x++)
			{
				var mirroredX = mirror ? width - 1 - x : x;
				var (targetX, targetY) = rotation switch
				{
					90 => (height - 1 - y, mirroredX),
					180 => (width - 1 - mirroredX, height - 1 - y),
					270 => (y, width - 1 - mirroredX),
					_ => (mirroredX, y)
				};
				Buffer.BlockCopy(source, (y * width + x) * 4, result, (targetY * outWidth + targetX) * 4, 4);
			}
		}

		return (result, outWidth, outHeight);
	}

	private static byte[] Skia(byte[] pixels, int width, int height, SKEncodedImageFormat format, int quality)
	{
		var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
		using var data = SKData.CreateCopy(pixels);
		using var image = SKImage.FromPixels(info, data, width * 4);
		using var encoded = image.Encode(format, quality);
		return encoded.ToArray();
	}

	private static byte[] Bmp(byte[] pixels, int width, int height)
	{
		const int headerSize = 54;
		var stride = width * 3 + 3 & ~3;
		var result = new byte[headerSize + stride * height];
		var span = result.AsSpan();
		span[0] = (byte)'B';
		span[1] = (byte)'M';
		BinaryPrimitives.WriteInt32LittleEndian(span[2..], result.Length);
		BinaryPrimitives.WriteInt32LittleEndian(span[10..], headerSize);
		BinaryPrimitives.WriteInt32LittleEndian(span[14..], 40);
		BinaryPrimitives.WriteInt32LittleEndian(span[18..], width);
		BinaryPrimitives.WriteInt32LittleEndian(span[22..], height);
		BinaryPrimitives.WriteInt16LittleEndian(span[26..], 1);
		BinaryPrimitives.WriteInt16LittleEndian(span[28..], 24);
		BinaryPrimitives.WriteInt32LittleEndian(span[34..], stride * height);
		for (var y = 0; y < height; y++)
		{
			var row = headerSize + (height - 1 - y) * stride;
			for (var x = 0; x < width; x++)
			{
				var source = (y * width + x) * 4;
				result[row + x * 3] = pixels[source + 2];
				result[row + x * 3 + 1] = pixels[source + 1];
				result[row + x * 3 + 2] = pixels[source];
			}
		}

		return result;
	}
}
