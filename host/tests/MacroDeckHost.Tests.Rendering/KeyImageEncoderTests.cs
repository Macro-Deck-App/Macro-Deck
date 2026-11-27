using System.Buffers.Binary;
using MacroDeckHost.Rendering;
using SkiaSharp;

namespace MacroDeckHost.Tests.Rendering;

public sealed class KeyImageEncoderTests
{
	private static readonly (byte R, byte G, byte B)[] _source =
		[(255, 0, 0), (0, 255, 0), (0, 0, 255), (255, 255, 255), (0, 0, 0), (128, 128, 128)];

	private static byte[] Picture()
	{
		return _source.SelectMany(pixel => new byte[] { pixel.R, pixel.G, pixel.B, 255 }).ToArray();
	}

	private static (byte, byte, byte) At(byte[] rgba, int width, int x, int y)
	{
		var index = (y * width + x) * 4;
		return (rgba[index], rgba[index + 1], rgba[index + 2]);
	}

	[Test]
	public async Task Raw_rgba_is_the_picture_top_down_and_opaque()
	{
		var input = Picture();
		input[3] = 0;

		var encoded = KeyImageEncoder.Encode(input, 3, 2, KeyImageFormat.Rgba);

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(encoded.ContentType, Is.EqualTo("application/x-macrodeck-rgba"));
			Assert.That(encoded.Data, Has.Length.EqualTo(3 * 2 * 4));
			Assert.That(At(encoded.Data, 3, 2, 0), Is.EqualTo(((byte)0, (byte)0, (byte)255)), "top right stays blue");
			Assert.That(At(encoded.Data, 3, 0, 0),
				Is.EqualTo(((byte)0, (byte)0, (byte)0)),
				"a transparent pixel is flattened onto black");
			Assert.That(Enumerable.Range(0, 6).All(index => encoded.Data[index * 4 + 3] == 255),
				Is.True,
				"every pixel is opaque");
			await Task.CompletedTask;
		});
	}

	[TestCase(false, 0, 3, 2, 255, 0, 0)]
	[TestCase(true, 0, 3, 2, 0, 0, 255)]
	[TestCase(false, 90, 2, 3, 255, 255, 255)]
	[TestCase(false, 180, 3, 2, 128, 128, 128)]
	[TestCase(false, 270, 2, 3, 0, 0, 255)]
	[TestCase(true, 90, 2, 3, 128, 128, 128)]
	public async Task Mirror_comes_before_the_clockwise_rotation(
		bool mirror,
		int rotation,
		int width,
		int height,
		int r,
		int g,
		int b)
	{
		var encoded = KeyImageEncoder.Encode(Picture(), 3, 2, KeyImageFormat.Rgba, mirror, rotation);

		await Assert.MultipleAsync(async () =>
		{
			Assert.That((encoded.Width, encoded.Height), Is.EqualTo((width, height)));
			Assert.That(At(encoded.Data, width, 0, 0), Is.EqualTo(((byte)r, (byte)g, (byte)b)), "top left pixel");
			await Task.CompletedTask;
		});
	}

	[Test]
	public async Task Png_decodes_back_to_the_picture()
	{
		var encoded = KeyImageEncoder.Encode(Picture(), 3, 2, KeyImageFormat.Png);

		using var decoded = SKBitmap.Decode(encoded.Data);
		await Assert.MultipleAsync(async () =>
		{
			Assert.That(encoded.ContentType, Is.EqualTo("image/png"));
			Assert.That((decoded.Width, decoded.Height), Is.EqualTo((3, 2)));
			Assert.That(decoded.GetPixel(2, 0), Is.EqualTo(new SKColor(0, 0, 255)));
			Assert.That(decoded.GetPixel(0, 1), Is.EqualTo(new SKColor(255, 255, 255)));
			await Task.CompletedTask;
		});
	}

	[Test]
	public async Task Jpeg_decodes_close_to_the_picture()
	{
		var pixels = Enumerable.Range(0, 16 * 16).SelectMany(_ => new byte[] { 200, 40, 40, 255 }).ToArray();

		var encoded = KeyImageEncoder.Encode(pixels, 16, 16, KeyImageFormat.Jpeg);

		using var decoded = SKBitmap.Decode(encoded.Data);
		var centre = decoded.GetPixel(8, 8);
		await Assert.MultipleAsync(async () =>
		{
			Assert.That(encoded.ContentType, Is.EqualTo("image/jpeg"));
			Assert.That(encoded.Data.Take(2), Is.EqualTo(new byte[] { 0xFF, 0xD8 }));
			Assert.That(Math.Abs(centre.Red - 200) + Math.Abs(centre.Green - 40) + Math.Abs(centre.Blue - 40),
				Is.LessThan(24));
			await Task.CompletedTask;
		});
	}

	[Test]
	public async Task Bmp_is_24_bit_bottom_up_with_padded_rows()
	{
		var encoded = KeyImageEncoder.Encode(Picture(), 3, 2, KeyImageFormat.Bmp);
		var data = encoded.Data;
		var offset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(10));
		const int stride = 12;

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(encoded.ContentType, Is.EqualTo("image/bmp"));
			Assert.That(data.Take(2), Is.EqualTo("BM"u8.ToArray()));
			Assert.That(BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(28)), Is.EqualTo(24), "bits per pixel");
			Assert.That(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(22)),
				Is.EqualTo(2),
				"positive height: bottom-up");
			Assert.That(data, Has.Length.EqualTo(offset + stride * 2));
			Assert.That(data.Skip(offset).Take(3),
				Is.EqualTo(new byte[] { 255, 255, 255 }),
				"the first stored row is the bottom one");
			Assert.That(data.Skip(offset + stride).Take(3),
				Is.EqualTo(new byte[] { 0, 0, 255 }),
				"pixels are stored blue, green, red");
			await Task.CompletedTask;
		});
	}
}
