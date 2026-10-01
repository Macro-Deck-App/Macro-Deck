using System.Buffers.Binary;
using System.IO.Compression;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests.Support;

internal sealed class PngProbe
{
	private readonly byte[] _pixels;
	private readonly int _bytesPerPixel;

	private PngProbe(int width, int height, int colorType, byte[] pixels, int bytesPerPixel)
	{
		Width = width;
		Height = height;
		ColorType = colorType;
		_pixels = pixels;
		_bytesPerPixel = bytesPerPixel;
	}

	public int Width { get; }

	public int Height { get; }

	public int ColorType { get; }

	public byte[] FirstPixel => PixelAt(0, 0);

	public byte[] PixelAt(int x, int y) => _pixels.AsSpan(((y * Width) + x) * _bytesPerPixel, _bytesPerPixel).ToArray();

	public static PngProbe Read(string path)
	{
		var bytes = File.ReadAllBytes(path);
		var width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16));
		var height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20));
		var colorType = bytes[25];
		var data = new MemoryStream();

		for (var offset = 8; offset < bytes.Length;)
		{
			var length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset));

			if (bytes.AsSpan(offset + 4, 4).SequenceEqual("IDAT"u8))
			{
				data.Write(bytes, offset + 8, length);
			}

			offset += 12 + length;
		}

		data.Position = 0;
		using var inflated = new ZLibStream(data, CompressionMode.Decompress);
		var bytesPerPixel = colorType == 6 ? 4 : 3;
		var stride = width * bytesPerPixel;
		var pixels = new byte[stride * height];
		var row = new byte[stride + 1];

		for (var y = 0; y < height; y++)
		{
			inflated.ReadExactly(row);

			for (var index = 0; index < stride; index++)
			{
				var left = index >= bytesPerPixel ? pixels[(y * stride) + index - bytesPerPixel] : 0;
				var up = y > 0 ? pixels[((y - 1) * stride) + index] : 0;
				var upLeft = y > 0 && index >= bytesPerPixel ? pixels[((y - 1) * stride) + index - bytesPerPixel] : 0;
				var predicted = row[0] switch
				{
					1 => left,
					2 => up,
					3 => (left + up) / 2,
					4 => Paeth(left, up, upLeft),
					_ => 0
				};
				pixels[(y * stride) + index] = (byte)(row[index + 1] + predicted);
			}
		}

		return new PngProbe(width, height, colorType, pixels, bytesPerPixel);
	}

	private static int Paeth(int a, int b, int c)
	{
		var p = a + b - c;
		var pa = Math.Abs(p - a);
		var pb = Math.Abs(p - b);
		var pc = Math.Abs(p - c);

		return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
	}
}
