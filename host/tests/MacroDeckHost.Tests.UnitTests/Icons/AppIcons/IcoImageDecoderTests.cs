using System.Buffers.Binary;
using MacroDeckHost.Infrastructure.Icons.AppIcons;
using SkiaSharp;

namespace MacroDeckHost.Tests.UnitTests.Icons.AppIcons;

[TestFixture]
public class IcoImageDecoderTests
{
	private const int DirectoryEntryLength = 16;

	[Test]
	public void LooksLikeIco_IcoDirectoryHeader_ReturnsTrue()
	{
		var ico = BuildIco((32, CreatePng(32)));

		Assert.That(IcoImageDecoder.LooksLikeIco(ico), Is.True);
	}

	[Test]
	public void LooksLikeIco_Png_ReturnsFalse()
	{
		Assert.That(IcoImageDecoder.LooksLikeIco(CreatePng(32)), Is.False);
	}

	[Test]
	public void LooksLikeIco_CursorResourceType_ReturnsFalse()
	{
		var cursor = BuildIco((32, CreatePng(32)));
		cursor[2] = 2;

		Assert.That(IcoImageDecoder.LooksLikeIco(cursor), Is.False);
	}

	[Test]
	public void LooksLikeIco_ZeroImageCount_ReturnsFalse()
	{
		byte[] header = [0, 0, 1, 0, 0, 0];

		Assert.That(IcoImageDecoder.LooksLikeIco(header), Is.False);
	}

	[Test]
	public void LooksLikeIco_TooShortInput_ReturnsFalse()
	{
		Assert.Multiple(() =>
		{
			Assert.That(IcoImageDecoder.LooksLikeIco([]), Is.False);
			Assert.That(IcoImageDecoder.LooksLikeIco([0, 0, 1, 0, 1]), Is.False);
		});
	}

	[Test]
	public void TryDecodeToPng_MultiSizeIco_ReturnsLargestFrameEncodedAsPng()
	{
		var ico = BuildIco((32, CreatePng(32)), (128, CreatePng(128)), (64, CreatePng(64)));

		var png = IcoImageDecoder.TryDecodeToPng(ico);

		Assert.That(png, Is.Not.Null);
		using var data = SKData.CreateCopy(png);
		using var codec = SKCodec.Create(data);
		Assert.That(codec, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(codec!.EncodedFormat, Is.EqualTo(SKEncodedImageFormat.Png));
			Assert.That(codec.Info.Width, Is.EqualTo(128));
			Assert.That(codec.Info.Height, Is.EqualTo(128));
		});
	}

	[Test]
	public void TryDecodeToPng_SingleFrameIco_ReturnsThatFrame()
	{
		var ico = BuildIco((48, CreatePng(48)));

		var png = IcoImageDecoder.TryDecodeToPng(ico);

		Assert.That(png, Is.Not.Null);
		using var data = SKData.CreateCopy(png);
		using var codec = SKCodec.Create(data);
		Assert.That(codec, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(codec!.EncodedFormat, Is.EqualTo(SKEncodedImageFormat.Png));
			Assert.That(codec.Info.Width, Is.EqualTo(48));
		});
	}

	[Test]
	public void TryDecodeToPng_NotAnImage_ReturnsNull()
	{
		Assert.Multiple(() =>
		{
			Assert.That(IcoImageDecoder.TryDecodeToPng([]), Is.Null);
			Assert.That(IcoImageDecoder.TryDecodeToPng([0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08]), Is.Null);
		});
	}

	[Test]
	public void TryDecodeToPng_DirectoryEntryPointsOutsideBuffer_ReturnsNull()
	{
		var ico = BuildIco((32, CreatePng(32)));
		BinaryPrimitives.WriteUInt32LittleEndian(ico.AsSpan(6 + 12), (uint)ico.Length + 1024);

		Assert.That(IcoImageDecoder.TryDecodeToPng(ico), Is.Null);
	}

	private static byte[] BuildIco(params (int Size, byte[] Data)[] frames)
	{
		using var stream = new MemoryStream();
		WriteUInt16LittleEndian(stream, 0);
		WriteUInt16LittleEndian(stream, 1);
		WriteUInt16LittleEndian(stream, (ushort)frames.Length);

		var offset = 6 + (frames.Length * DirectoryEntryLength);
		foreach (var frame in frames)
		{
			stream.WriteByte(frame.Size >= 256 ? (byte)0 : (byte)frame.Size);
			stream.WriteByte(frame.Size >= 256 ? (byte)0 : (byte)frame.Size);
			stream.WriteByte(0);
			stream.WriteByte(0);
			WriteUInt16LittleEndian(stream, 1);
			WriteUInt16LittleEndian(stream, 32);
			WriteUInt32LittleEndian(stream, (uint)frame.Data.Length);
			WriteUInt32LittleEndian(stream, (uint)offset);
			offset += frame.Data.Length;
		}

		foreach (var frame in frames)
		{
			stream.Write(frame.Data);
		}

		return stream.ToArray();
	}

	private static void WriteUInt16LittleEndian(Stream stream, ushort value)
	{
		Span<byte> buffer = stackalloc byte[2];
		BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
		stream.Write(buffer);
	}

	private static void WriteUInt32LittleEndian(Stream stream, uint value)
	{
		Span<byte> buffer = stackalloc byte[4];
		BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
		stream.Write(buffer);
	}

	private static byte[] CreatePng(int size)
	{
		using var bitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
		bitmap.Erase(SKColors.CornflowerBlue);
		using var image = SKImage.FromBitmap(bitmap);
		using var data = image.Encode(SKEncodedImageFormat.Png, 100);
		return data.ToArray();
	}
}
