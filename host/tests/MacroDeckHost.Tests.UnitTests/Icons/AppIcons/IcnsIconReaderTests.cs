using System.Buffers.Binary;
using System.Text;
using MacroDeckHost.Infrastructure.Icons.AppIcons;
using SkiaSharp;

namespace MacroDeckHost.Tests.UnitTests.Icons.AppIcons;

[TestFixture]
public class IcnsIconReaderTests
{
	private const int ChunkHeaderLength = 8;

	private static readonly byte[] _jpeg2000Signature = [0x00, 0x00, 0x00, 0x0C, 0x6A, 0x50, 0x20, 0x20];

	[Test]
	public void TryExtractBestIcon_MultiplePngEntries_ReturnsHighestResolution()
	{
		var icns = BuildIcns(new IcnsChunk("icp4", CreatePng(16)),
			new IcnsChunk("ic09", CreatePng(512)),
			new IcnsChunk("ic07", CreatePng(128)));

		var result = IcnsIconReader.TryExtractBestIcon(icns);

		Assert.That(result, Is.Not.Null);
		Assert.That(ReadPngWidth(result), Is.EqualTo(512));
	}

	[Test]
	public void TryExtractBestIcon_OsTypeContradictsPayload_TrustsThePayload()
	{
		var icns = BuildIcns(new IcnsChunk("ic07", CreatePng(32)), new IcnsChunk("icp4", CreatePng(256)));

		var result = IcnsIconReader.TryExtractBestIcon(icns);

		Assert.That(result, Is.Not.Null);
		Assert.That(ReadPngWidth(result), Is.EqualTo(256));
	}

	[Test]
	public void TryExtractBestIcon_EqualWidths_PrefersLargerPayload()
	{
		var flat = CreatePng(64);
		var noisy = CreatePng(64, true);
		var icns = BuildIcns(new IcnsChunk("ic11", flat), new IcnsChunk("ic12", noisy));

		var result = IcnsIconReader.TryExtractBestIcon(icns);

		Assert.Multiple(() =>
		{
			Assert.That(noisy.Length, Is.GreaterThan(flat.Length));
			Assert.That(result, Is.EqualTo(noisy));
		});
	}

	[Test]
	public void TryExtractBestIcon_LegacyRawEntriesOnly_ReturnsNull()
	{
		var icns = BuildIcns(new IcnsChunk("is32", CreateFiller(16 * 16 * 3)),
			new IcnsChunk("s8mk", CreateFiller(16 * 16)),
			new IcnsChunk("il32", CreateFiller(32 * 32 * 3)),
			new IcnsChunk("it32", CreateFiller(64)));

		Assert.That(IcnsIconReader.TryExtractBestIcon(icns), Is.Null);
	}

	[Test]
	public void TryExtractBestIcon_Jpeg2000Entry_ReturnsNull()
	{
		var payload = new byte[64];
		_jpeg2000Signature.CopyTo(payload, 0);
		var icns = BuildIcns(new IcnsChunk("ic08", payload));

		Assert.That(IcnsIconReader.TryExtractBestIcon(icns), Is.Null);
	}

	[Test]
	public void TryExtractBestIcon_PngInsideMetadataChunk_IsIgnored()
	{
		var icns = BuildIcns(new IcnsChunk("TOC ", CreatePng(512)),
			new IcnsChunk("icnV", CreatePng(256)),
			new IcnsChunk("ic07", CreatePng(128)));

		var result = IcnsIconReader.TryExtractBestIcon(icns);

		Assert.That(result, Is.Not.Null);
		Assert.That(ReadPngWidth(result), Is.EqualTo(128));
	}

	[Test]
	public void TryExtractBestIcon_ChunkLengthOverrunsContainer_ReturnsNull()
	{
		var icns = BuildIcns(new IcnsChunk("ic09", CreatePng(512), 0xFFFF));

		Assert.That(IcnsIconReader.TryExtractBestIcon(icns), Is.Null);
	}

	[Test]
	public void TryExtractBestIcon_ChunkLengthBelowHeaderLength_ReturnsNull()
	{
		var icns = BuildIcns(new IcnsChunk("ic09", CreatePng(512), 4));

		Assert.That(IcnsIconReader.TryExtractBestIcon(icns), Is.Null);
	}

	[Test]
	public void TryExtractBestIcon_TruncatedChunkAfterValidEntry_ReturnsWhatWasFound()
	{
		var icns = BuildIcns(new IcnsChunk("ic07", CreatePng(128)),
			new IcnsChunk("ic09", CreatePng(512), 0x7FFFFFFF));

		var result = IcnsIconReader.TryExtractBestIcon(icns);

		Assert.That(result, Is.Not.Null);
		Assert.That(ReadPngWidth(result), Is.EqualTo(128));
	}

	[Test]
	public void TryExtractBestIcon_PayloadTruncatedByContainerLength_ReturnsNull()
	{
		var icns = BuildIcns(new IcnsChunk("ic09", CreatePng(512)));
		var truncated = icns[..(icns.Length / 2)];

		Assert.That(IcnsIconReader.TryExtractBestIcon(truncated), Is.Null);
	}

	[Test]
	public void TryExtractBestIcon_MissingMagic_ReturnsNull()
	{
		var icns = BuildIcns(new IcnsChunk("ic09", CreatePng(512)));
		icns[0] = (byte)'X';

		Assert.That(IcnsIconReader.TryExtractBestIcon(icns), Is.Null);
	}

	[Test]
	public void TryExtractBestIcon_EmptyInput_ReturnsNull()
	{
		Assert.Multiple(() =>
		{
			Assert.That(IcnsIconReader.TryExtractBestIcon([]), Is.Null);
			Assert.That(IcnsIconReader.TryExtractBestIcon(Encoding.ASCII.GetBytes("icns")), Is.Null);
		});
	}

	private static byte[] BuildIcns(params IcnsChunk[] chunks)
	{
		using var body = new MemoryStream();
		foreach (var chunk in chunks)
		{
			body.Write(Encoding.ASCII.GetBytes(chunk.Type));
			WriteUInt32BigEndian(body, chunk.DeclaredLength ?? (uint)(ChunkHeaderLength + chunk.Payload.Length));
			body.Write(chunk.Payload);
		}

		var bodyBytes = body.ToArray();
		using var container = new MemoryStream();
		container.Write("icns"u8);
		WriteUInt32BigEndian(container, (uint)(ChunkHeaderLength + bodyBytes.Length));
		container.Write(bodyBytes);
		return container.ToArray();
	}

	private static void WriteUInt32BigEndian(Stream stream, uint value)
	{
		Span<byte> buffer = stackalloc byte[4];
		BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
		stream.Write(buffer);
	}

	private static int ReadPngWidth(byte[]? png)
	{
		Assert.That(png, Is.Not.Null);
		return (int)BinaryPrimitives.ReadUInt32BigEndian(new ReadOnlySpan<byte>(png).Slice(16, 4));
	}

	private static byte[] CreateFiller(int length)
	{
		var bytes = new byte[length];
		for (var i = 0; i < length; i++)
		{
			bytes[i] = (byte)(i % 251);
		}

		return bytes;
	}

	private static byte[] CreatePng(int size, bool noisy = false)
	{
		using var bitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
		if (noisy)
		{
			var random = new Random(1234);
			for (var y = 0; y < size; y++)
			{
				for (var x = 0; x < size; x++)
				{
					var color = new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
					bitmap.SetPixel(x, y, color);
				}
			}
		}
		else
		{
			bitmap.Erase(SKColors.CornflowerBlue);
		}

		using var image = SKImage.FromBitmap(bitmap);
		using var data = image.Encode(SKEncodedImageFormat.Png, 100);
		return data.ToArray();
	}

	private sealed record IcnsChunk(string Type, byte[] Payload, uint? DeclaredLength = null);
}
