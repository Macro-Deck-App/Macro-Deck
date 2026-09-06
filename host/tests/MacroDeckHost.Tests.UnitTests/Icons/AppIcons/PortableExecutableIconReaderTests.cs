using System.Buffers.Binary;
using System.Text;
using MacroDeckHost.Infrastructure.Icons.AppIcons;
using SkiaSharp;

namespace MacroDeckHost.Tests.UnitTests.Icons.AppIcons;

[TestFixture]
public class PortableExecutableIconReaderTests
{
	private const int PeHeaderOffset = 0x80;
	private const int SectionRawOffset = 0x400;
	private const uint ResourceRva = 0x1000;
	private const int DirectoryHeaderSize = 16;
	private const int DirectoryEntrySize = 8;
	private const int DataEntrySize = 16;
	private const int GroupHeaderSize = 6;
	private const int GroupEntrySize = 14;
	private const int DibHeaderSize = 40;
	private const int IconHeaderSize = 22;
	private const uint RtIcon = 3;
	private const uint RtGroupIcon = 14;
	private const uint RtString = 6;
	private const uint GroupResourceId = 1;
	private const uint LanguageId = 0x409;

	private static readonly byte[] _pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

	[Test]
	public void TryExtractBestIcon_GroupIcon_PicksHighestResolutionEntry()
	{
		var small = new IconResource(1, BuildIconDib(16, 16, 0x11));
		var large = new IconResource(2, BuildIconDib(48, 48, 0x22));
		var image = BuildPortableExecutable(false,
			[new GroupMember(16, 16, 32, 1), new GroupMember(48, 48, 32, 2)],
			[small, large]);

		var result = ExtractRequired(image);

		Assert.Multiple(() =>
		{
			Assert.That(result[6], Is.EqualTo(48), "width byte");
			Assert.That(result[7], Is.EqualTo(48), "height byte");
			Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(4)), Is.EqualTo(1), "image count");
			Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(18)),
				Is.EqualTo(IconHeaderSize),
				"image offset");
			Assert.That(result.AsSpan(IconHeaderSize).ToArray(), Is.EqualTo(large.Payload));
		});
	}

	[Test]
	public void TryExtractBestIcon_ZeroSizeByte_IsTreatedAs256()
	{
		var medium = new IconResource(1, BuildIconDib(48, 48, 0x11));
		var huge = new IconResource(2, BuildIconDib(256, 256, 0x22));
		var image = BuildPortableExecutable(false,
			[new GroupMember(48, 48, 32, 1), new GroupMember(0, 0, 32, 2)],
			[medium, huge]);

		var result = ExtractRequired(image);

		Assert.Multiple(() =>
		{
			Assert.That(result[6], Is.EqualTo(0), "256 is stored as a 0 size byte");
			Assert.That(result[7], Is.EqualTo(0), "256 is stored as a 0 size byte");
			Assert.That(result.AsSpan(IconHeaderSize).ToArray(), Is.EqualTo(huge.Payload));
		});
	}

	[Test]
	public void TryExtractBestIcon_PngResource_IsReturnedUnchanged()
	{
		var png = BuildPng(256, 256);
		var image = BuildPortableExecutable(false,
			[new GroupMember(48, 48, 32, 1), new GroupMember(0, 0, 32, 2)],
			[new IconResource(1, BuildIconDib(48, 48, 0x11)), new IconResource(2, png)]);

		var result = Extract(image);

		Assert.That(result, Is.EqualTo(png));
	}

	[Test]
	public void TryExtractBestIcon_Pe32PlusImage_IsParsed()
	{
		var large = new IconResource(2, BuildIconDib(64, 64, 0x33));
		var image = BuildPortableExecutable(true,
			[new GroupMember(16, 16, 32, 1), new GroupMember(64, 64, 32, 2)],
			[new IconResource(1, BuildIconDib(16, 16, 0x11)), large]);

		var result = ExtractRequired(image);

		Assert.Multiple(() =>
		{
			Assert.That(result[6], Is.EqualTo(64));
			Assert.That(result.AsSpan(IconHeaderSize).ToArray(), Is.EqualTo(large.Payload));
		});
	}

	[Test]
	public void TryExtractBestIcon_WithoutGroupResource_PicksLargestIconResource()
	{
		var large = new IconResource(2, BuildIconDib(64, 64, 0x44));
		var image = BuildPortableExecutable(false,
			null,
			[new IconResource(1, BuildIconDib(16, 16, 0x11)), large]);

		var result = ExtractRequired(image);

		Assert.Multiple(() =>
		{
			Assert.That(result[6], Is.EqualTo(64));
			Assert.That(result.AsSpan(IconHeaderSize).ToArray(), Is.EqualTo(large.Payload));
		});
	}

	[Test]
	public void TryExtractBestIcon_EqualSizedGroupEntries_PrefersHigherBitCount()
	{
		var trueColor = new IconResource(2, BuildIconDib(32, 32, 0x55));
		var image = BuildPortableExecutable(false,
			[new GroupMember(32, 32, 8, 1), new GroupMember(32, 32, 32, 2)],
			[new IconResource(1, BuildIconDib(32, 32, 0x11)), trueColor]);

		var result = ExtractRequired(image);

		Assert.That(result.AsSpan(IconHeaderSize).ToArray(), Is.EqualTo(trueColor.Payload));
	}

	[Test]
	public void TryExtractBestIcon_GroupEntryWithoutIconResource_FallsBackToNextEntry()
	{
		var present = new IconResource(1, BuildIconDib(32, 32, 0x66));
		var image = BuildPortableExecutable(false,
			[new GroupMember(64, 64, 32, 9), new GroupMember(32, 32, 32, 1)],
			[present]);

		var result = ExtractRequired(image);

		Assert.Multiple(() =>
		{
			Assert.That(result[6], Is.EqualTo(32));
			Assert.That(result.AsSpan(IconHeaderSize).ToArray(), Is.EqualTo(present.Payload));
		});
	}

	[Test]
	public void TryExtractBestIcon_WrappedIcon_IsDecodableBySkia()
	{
		var image = BuildPortableExecutable(false,
			[new GroupMember(32, 32, 32, 1)],
			[new IconResource(1, BuildIconDib(32, 32, 0x77))]);

		var result = ExtractRequired(image);

		using var bitmap = SKBitmap.Decode(result);
		Assert.That(bitmap, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(bitmap!.Width, Is.EqualTo(32));
			Assert.That(bitmap.Height, Is.EqualTo(32));
		});
	}

	[Test]
	public void TryExtractBestIcon_TruncatedImage_ReturnsNull()
	{
		var image = BuildPortableExecutable(false,
			[new GroupMember(48, 48, 32, 1)],
			[new IconResource(1, BuildIconDib(48, 48, 0x11))]);

		var truncated = image.AsSpan(0, image.Length / 2).ToArray();

		Assert.That(Extract(truncated), Is.Null);
	}

	[Test]
	public void TryExtractBestIcon_TruncatedIconPayload_ReturnsNull()
	{
		var image = BuildPortableExecutable(false,
			[new GroupMember(32, 32, 32, 1)],
			[new IconResource(1, new byte[8])]);

		Assert.That(Extract(image), Is.Null);
	}

	[Test]
	public void TryExtractBestIcon_NonPeInput_ReturnsNull()
	{
		var bytes = new byte[512];
		Array.Fill(bytes, (byte)0xAB);

		Assert.Multiple(() =>
		{
			Assert.That(Extract(bytes), Is.Null);
			Assert.That(Extract([]), Is.Null);
			Assert.That(Extract(Encoding.ASCII.GetBytes("MZ but far too short")), Is.Null);
		});
	}

	[Test]
	public void TryExtractBestIcon_MzWithoutPeSignature_ReturnsNull()
	{
		var image = BuildPortableExecutable(false,
			[new GroupMember(32, 32, 32, 1)],
			[new IconResource(1, BuildIconDib(32, 32, 0x11))]);
		image[PeHeaderOffset + 1] = (byte)'X';

		Assert.That(Extract(image), Is.Null);
	}

	[Test]
	public void TryExtractBestIcon_WithoutIconResources_ReturnsNull()
	{
		var image = BuildPortableExecutable(false,
			null,
			[new IconResource(1, BuildIconDib(32, 32, 0x11))],
			RtString);

		Assert.That(Extract(image), Is.Null);
	}

	[Test]
	public void TryExtractBestIcon_DibHeightOfIntMinValue_ReturnsNull()
	{
		var dib = BuildIconDib(32, 32, 0x11);
		BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), int.MinValue);
		var image = BuildPortableExecutable(false, [new GroupMember(32, 32, 32, 1)], [new IconResource(1, dib)]);

		Assert.That(Extract(image), Is.Null);
	}

	private static byte[]? Extract(byte[] image)
	{
		using var stream = new MemoryStream(image, false);
		return PortableExecutableIconReader.TryExtractBestIcon(stream);
	}

	private static byte[] ExtractRequired(byte[] image)
	{
		var result = Extract(image);
		Assert.That(result, Is.Not.Null);
		return result!;
	}

	private static byte[] BuildPortableExecutable(bool pe32Plus,
		IReadOnlyList<GroupMember>? group,
		IReadOnlyList<IconResource> icons,
		uint iconTypeId = RtIcon)
	{
		var resources = BuildResourceBlob(group, icons, iconTypeId);
		var optionalHeaderSize = pe32Plus ? 240 : 224;
		var image = new byte[SectionRawOffset + resources.Length];

		image[0] = (byte)'M';
		image[1] = (byte)'Z';
		BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(0x3C), PeHeaderOffset);

		image[PeHeaderOffset] = (byte)'P';
		image[PeHeaderOffset + 1] = (byte)'E';
		var fileHeader = PeHeaderOffset + 4;
		BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(fileHeader), pe32Plus ? (ushort)0x8664 : (ushort)0x014C);
		BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(fileHeader + 2), 1);
		BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(fileHeader + 16), (ushort)optionalHeaderSize);
		BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(fileHeader + 18), 0x2002);

		var optionalHeader = fileHeader + 20;
		var magic = pe32Plus ? (ushort)0x20B : (ushort)0x10B;
		BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(optionalHeader), magic);
		var directoriesOffset = pe32Plus ? 112 : 96;
		BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(optionalHeader + directoriesOffset - 4), 16);
		var resourceDirectory = optionalHeader + directoriesOffset + (2 * 8);
		BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(resourceDirectory), ResourceRva);
		BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(resourceDirectory + 4), (uint)resources.Length);

		var sectionHeader = optionalHeader + optionalHeaderSize;
		Encoding.ASCII.GetBytes(".rsrc").CopyTo(image, sectionHeader);
		BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(sectionHeader + 8), (uint)resources.Length);
		BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(sectionHeader + 12), ResourceRva);
		BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(sectionHeader + 16), (uint)resources.Length);
		BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(sectionHeader + 20), SectionRawOffset);

		resources.CopyTo(image, SectionRawOffset);
		return image;
	}

	private static byte[] BuildResourceBlob(IReadOnlyList<GroupMember>? group,
		IReadOnlyList<IconResource> icons,
		uint iconTypeId)
	{
		var typeCount = group is null ? 1 : 2;
		var cursor = DirectorySize(typeCount);

		var iconTypeOffset = cursor;
		cursor += DirectorySize(icons.Count);

		var iconLanguageOffsets = new int[icons.Count];
		for (var i = 0; i < icons.Count; i++)
		{
			iconLanguageOffsets[i] = cursor;
			cursor += DirectorySize(1);
		}

		var groupTypeOffset = cursor;
		var groupLanguageOffset = cursor + DirectorySize(1);
		if (group is not null)
		{
			cursor = groupLanguageOffset + DirectorySize(1);
		}

		var iconDataOffsets = new int[icons.Count];
		for (var i = 0; i < icons.Count; i++)
		{
			iconDataOffsets[i] = cursor;
			cursor += DataEntrySize;
		}

		var groupDataOffset = cursor;
		if (group is not null)
		{
			cursor += DataEntrySize;
		}

		var groupBytes = group is null ? [] : BuildGroupDirectory(group);
		var groupPayloadOffset = cursor;
		cursor += Align(groupBytes.Length);

		var iconPayloadOffsets = new int[icons.Count];
		for (var i = 0; i < icons.Count; i++)
		{
			iconPayloadOffsets[i] = cursor;
			cursor += Align(icons[i].Payload.Length);
		}

		var blob = new byte[cursor];
		WriteDirectoryHeader(blob, 0, typeCount);
		WriteEntry(blob, DirectoryHeaderSize, iconTypeId, iconTypeOffset, true);
		WriteDirectoryHeader(blob, iconTypeOffset, icons.Count);

		for (var i = 0; i < icons.Count; i++)
		{
			WriteEntry(blob,
				iconTypeOffset + DirectoryHeaderSize + (i * DirectoryEntrySize),
				icons[i].Id,
				iconLanguageOffsets[i],
				true);
			WriteLeafDirectory(blob, iconLanguageOffsets[i], iconDataOffsets[i]);
			WriteDataEntry(blob, iconDataOffsets[i], iconPayloadOffsets[i], icons[i].Payload.Length);
			icons[i].Payload.CopyTo(blob, iconPayloadOffsets[i]);
		}

		if (group is not null)
		{
			WriteEntry(blob, DirectoryHeaderSize + DirectoryEntrySize, RtGroupIcon, groupTypeOffset, true);
			WriteDirectoryHeader(blob, groupTypeOffset, 1);
			WriteEntry(blob, groupTypeOffset + DirectoryHeaderSize, GroupResourceId, groupLanguageOffset, true);
			WriteLeafDirectory(blob, groupLanguageOffset, groupDataOffset);
			WriteDataEntry(blob, groupDataOffset, groupPayloadOffset, groupBytes.Length);
			groupBytes.CopyTo(blob, groupPayloadOffset);
		}

		return blob;
	}

	private static void WriteDirectoryHeader(byte[] blob, int offset, int idEntryCount)
		=> BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(offset + 14), (ushort)idEntryCount);

	private static void WriteEntry(byte[] blob, int offset, uint id, int target, bool isDirectory)
	{
		BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(offset), id);
		var value = (uint)target | (isDirectory ? 0x8000_0000u : 0u);
		BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(offset + 4), value);
	}

	private static void WriteLeafDirectory(byte[] blob, int offset, int dataEntryOffset)
	{
		WriteDirectoryHeader(blob, offset, 1);
		WriteEntry(blob, offset + DirectoryHeaderSize, LanguageId, dataEntryOffset, false);
	}

	private static void WriteDataEntry(byte[] blob, int offset, int payloadOffset, int size)
	{
		BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(offset), ResourceRva + (uint)payloadOffset);
		BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(offset + 4), (uint)size);
	}

	private static byte[] BuildGroupDirectory(IReadOnlyList<GroupMember> members)
	{
		var bytes = new byte[GroupHeaderSize + (members.Count * GroupEntrySize)];
		BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), 1);
		BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), (ushort)members.Count);
		for (var i = 0; i < members.Count; i++)
		{
			var offset = GroupHeaderSize + (i * GroupEntrySize);
			bytes[offset] = members[i].Width;
			bytes[offset + 1] = members[i].Height;
			BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + 4), 1);
			BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + 6), members[i].BitCount);
			BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + 12), (ushort)members[i].IconId);
		}

		return bytes;
	}

	private static byte[] BuildIconDib(int width, int height, byte fill)
	{
		var maskStride = ((width + 31) / 32) * 4;
		var pixels = width * height * 4;
		var bytes = new byte[DibHeaderSize + pixels + (height * maskStride)];
		BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0), DibHeaderSize);
		BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), width);
		BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), height * 2);
		BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12), 1);
		BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(14), 32);
		BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), (uint)pixels);
		for (var i = 0; i < pixels; i += 4)
		{
			bytes[DibHeaderSize + i] = fill;
			bytes[DibHeaderSize + i + 1] = fill;
			bytes[DibHeaderSize + i + 2] = fill;
			bytes[DibHeaderSize + i + 3] = 0xFF;
		}

		return bytes;
	}

	private static byte[] BuildPng(int width, int height)
	{
		var bytes = new byte[_pngSignature.Length + 25];
		_pngSignature.CopyTo(bytes, 0);
		BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), 13);
		Encoding.ASCII.GetBytes("IHDR").CopyTo(bytes, 12);
		BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), (uint)width);
		BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), (uint)height);
		bytes[24] = 8;
		bytes[25] = 6;
		return bytes;
	}

	private static int DirectorySize(int entryCount) => DirectoryHeaderSize + (entryCount * DirectoryEntrySize);

	private static int Align(int size) => (size + 3) & ~3;

	private sealed record IconResource(uint Id, byte[] Payload);

	private sealed record GroupMember(byte Width, byte Height, ushort BitCount, uint IconId);
}
