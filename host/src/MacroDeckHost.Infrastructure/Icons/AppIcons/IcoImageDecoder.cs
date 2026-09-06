using System.Buffers.Binary;
using SkiaSharp;

namespace MacroDeckHost.Infrastructure.Icons.AppIcons;

internal static class IcoImageDecoder
{
	private const int DirectoryHeaderLength = 6;
	private const int IcoResourceType = 1;
	private const int PngQuality = 100;

	public static bool LooksLikeIco(ReadOnlySpan<byte> bytes)
	{
		if (bytes.Length < DirectoryHeaderLength)
		{
			return false;
		}

		return BinaryPrimitives.ReadUInt16LittleEndian(bytes) == 0 &&
			BinaryPrimitives.ReadUInt16LittleEndian(bytes[2..]) == IcoResourceType &&
			BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]) > 0;
	}

	public static byte[]? TryDecodeToPng(byte[] bytes)
	{
		try
		{
			using var source = SKData.CreateCopy(bytes);
			using var codec = SKCodec.Create(source);
			if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
			{
				return null;
			}

			using var bitmap = SKBitmap.Decode(codec);
			if (bitmap is null)
			{
				return null;
			}

			using var image = SKImage.FromBitmap(bitmap);
			if (image is null)
			{
				return null;
			}

			using var encoded = image.Encode(SKEncodedImageFormat.Png, PngQuality);
			return encoded?.ToArray();
		}
		catch (Exception)
		{
			return null;
		}
	}
}
