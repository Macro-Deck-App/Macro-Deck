using System.Buffers.Binary;
using System.IO.Compression;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests.Support;

internal sealed record PngProbe(int Width, int Height, int ColorType, byte[] FirstPixel)
{
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
		var row = new byte[1 + (width * bytesPerPixel)];
		inflated.ReadExactly(row);

		// Every PNG filter leaves the very first pixel as stored, because its left and upper neighbours are zero.
		var pixel = row.AsSpan(1, bytesPerPixel).ToArray();

		return new PngProbe(width, height, colorType, pixel);
	}
}
