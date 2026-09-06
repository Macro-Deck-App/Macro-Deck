using System.Buffers.Binary;

namespace MacroDeckHost.Infrastructure.Icons.AppIcons;

internal static class PortableExecutableIconReader
{
	private const int DosHeaderSize = 64;
	private const int PeHeaderOffsetLocation = 0x3C;
	private const int PeSignatureSize = 4;
	private const int FileHeaderSize = 20;
	private const int SectionHeaderSize = 40;
	private const ushort Pe32Magic = 0x10B;
	private const ushort Pe32PlusMagic = 0x20B;
	private const int Pe32DataDirectoriesOffset = 96;
	private const int Pe32PlusDataDirectoriesOffset = 112;
	private const int DataDirectoryEntrySize = 8;
	private const int ResourceDataDirectoryIndex = 2;
	private const int ResourceDirectorySize = 16;
	private const int ResourceDirectoryEntrySize = 8;
	private const int ResourceDataEntrySize = 16;
	private const uint ResourceHighBit = 0x8000_0000;
	private const uint RtIcon = 3;
	private const uint RtGroupIcon = 14;
	private const int GroupHeaderSize = 6;
	private const int GroupEntrySize = 14;
	private const int BitmapInfoHeaderSize = 40;
	private const int PngHeaderSize = 24;
	private const int IconDirSize = 6;
	private const int IconDirEntrySize = 16;
	private const int MaxSectionCount = 96;
	private const int MaxDirectoryEntries = 4096;
	private const int MaxGroupEntries = 512;
	private const int MaxPayloadBytes = 8 * 1024 * 1024;
	private const int MaxIconDimension = 4096;
	private const int PngBitCount = 32;

	private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

	public static byte[]? TryExtractBestIcon(Stream stream)
	{
		try
		{
			return ExtractBestIcon(stream);
		}
		catch (IOException)
		{
			return null;
		}
		catch (ArgumentException)
		{
			return null;
		}
	}

	private static byte[]? ExtractBestIcon(Stream stream)
	{
		var layout = TryReadLayout(stream);
		if (layout is null)
		{
			return null;
		}

		if (!TryResolveRva(layout, layout.ResourceRva, out var resourceBase, out var availableBytes))
		{
			return null;
		}

		var resourceSize = Math.Min(layout.ResourceSize, availableBytes);
		var types = TryReadDirectory(stream, resourceBase, resourceSize, 0);
		if (types is null)
		{
			return null;
		}

		var iconType = FindTypeDirectory(types, RtIcon);
		if (iconType is null)
		{
			return null;
		}

		var icons = TryReadDirectory(stream, resourceBase, resourceSize, iconType.Value.Offset);
		if (icons is null)
		{
			return null;
		}

		var payload = TryExtractFromGroup(stream, layout, resourceBase, resourceSize, types, icons) ??
			TryExtractLargestIcon(stream, layout, resourceBase, resourceSize, icons);
		if (payload is null)
		{
			return null;
		}

		return StartsWithPngSignature(payload) ? payload : TryWrapDibInIcon(payload);
	}

	private static PeLayout? TryReadLayout(Stream stream)
	{
		Span<byte> dosHeader = stackalloc byte[DosHeaderSize];
		if (!TryReadAt(stream, 0, dosHeader) || dosHeader[0] != (byte)'M' || dosHeader[1] != (byte)'Z')
		{
			return null;
		}

		var peOffset = BinaryPrimitives.ReadUInt32LittleEndian(dosHeader[PeHeaderOffsetLocation..]);
		Span<byte> coffHeader = stackalloc byte[PeSignatureSize + FileHeaderSize];
		if (!TryReadAt(stream, peOffset, coffHeader))
		{
			return null;
		}

		if (coffHeader[0] != (byte)'P' || coffHeader[1] != (byte)'E' || coffHeader[2] != 0 || coffHeader[3] != 0)
		{
			return null;
		}

		var sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(coffHeader[(PeSignatureSize + 2)..]);
		var optionalHeaderSize = BinaryPrimitives.ReadUInt16LittleEndian(coffHeader[(PeSignatureSize + 16)..]);
		if (sectionCount is 0 || sectionCount > MaxSectionCount)
		{
			return null;
		}

		var optionalHeaderOffset = (long)peOffset + PeSignatureSize + FileHeaderSize;
		if (!TryReadResourceDirectoryEntry(stream, optionalHeaderOffset, optionalHeaderSize, out var rva, out var size))
		{
			return null;
		}

		var sections = new Section[sectionCount];
		var sectionTableOffset = optionalHeaderOffset + optionalHeaderSize;
		Span<byte> sectionHeader = stackalloc byte[SectionHeaderSize];
		for (var i = 0; i < sectionCount; i++)
		{
			if (!TryReadAt(stream, sectionTableOffset + ((long)i * SectionHeaderSize), sectionHeader))
			{
				return null;
			}

			sections[i] = new Section(BinaryPrimitives.ReadUInt32LittleEndian(sectionHeader[12..]),
				BinaryPrimitives.ReadUInt32LittleEndian(sectionHeader[8..]),
				BinaryPrimitives.ReadUInt32LittleEndian(sectionHeader[16..]),
				BinaryPrimitives.ReadUInt32LittleEndian(sectionHeader[20..]));
		}

		return new PeLayout(sections, rva, size);
	}

	private static bool TryReadResourceDirectoryEntry(Stream stream,
		long optionalHeaderOffset,
		ushort optionalHeaderSize,
		out uint rva,
		out uint size)
	{
		rva = 0;
		size = 0;

		const int prefixSize
			= Pe32PlusDataDirectoriesOffset + ((ResourceDataDirectoryIndex + 1) * DataDirectoryEntrySize);
		Span<byte> buffer = stackalloc byte[prefixSize];
		var wanted = Math.Min(prefixSize, (int)optionalHeaderSize);
		if (wanted < sizeof(ushort))
		{
			return false;
		}

		var prefix = buffer[..wanted];
		if (!TryReadAt(stream, optionalHeaderOffset, prefix))
		{
			return false;
		}

		var directoriesOffset = BinaryPrimitives.ReadUInt16LittleEndian(prefix) switch
		{
			Pe32Magic => Pe32DataDirectoriesOffset,
			Pe32PlusMagic => Pe32PlusDataDirectoriesOffset,
			_ => -1,
		};

		if (directoriesOffset < 0)
		{
			return false;
		}

		var countOffset = directoriesOffset - sizeof(uint);
		var entryOffset = directoriesOffset + (ResourceDataDirectoryIndex * DataDirectoryEntrySize);
		if (entryOffset + DataDirectoryEntrySize > prefix.Length)
		{
			return false;
		}

		if (BinaryPrimitives.ReadUInt32LittleEndian(prefix[countOffset..]) <= ResourceDataDirectoryIndex)
		{
			return false;
		}

		rva = BinaryPrimitives.ReadUInt32LittleEndian(prefix[entryOffset..]);
		size = BinaryPrimitives.ReadUInt32LittleEndian(prefix[(entryOffset + sizeof(uint))..]);
		return rva is not 0 && size is not 0;
	}

	private static bool TryResolveRva(PeLayout layout, uint rva, out long offset, out long availableBytes)
	{
		foreach (var section in layout.Sections)
		{
			var start = (long)section.VirtualAddress;
			var end = start + Math.Max(section.VirtualSize, section.SizeOfRawData);
			if (rva < start || rva >= end)
			{
				continue;
			}

			var delta = rva - start;
			if (delta >= section.SizeOfRawData)
			{
				break;
			}

			offset = section.PointerToRawData + delta;
			availableBytes = section.SizeOfRawData - delta;
			return true;
		}

		offset = -1;
		availableBytes = 0;
		return false;
	}

	private static List<ResourceEntry>? TryReadDirectory(Stream stream,
		long resourceBase,
		long resourceSize,
		uint directoryOffset)
	{
		if (directoryOffset + ResourceDirectorySize > resourceSize)
		{
			return null;
		}

		Span<byte> header = stackalloc byte[ResourceDirectorySize];
		if (!TryReadAt(stream, resourceBase + directoryOffset, header))
		{
			return null;
		}

		var namedEntries = BinaryPrimitives.ReadUInt16LittleEndian(header[12..]);
		var idEntries = BinaryPrimitives.ReadUInt16LittleEndian(header[14..]);
		var total = namedEntries + idEntries;
		if (total is 0 || total > MaxDirectoryEntries)
		{
			return null;
		}

		var entriesOffset = directoryOffset + ResourceDirectorySize;
		var entriesSize = (long)total * ResourceDirectoryEntrySize;
		if (entriesOffset + entriesSize > resourceSize)
		{
			return null;
		}

		var buffer = new byte[entriesSize];
		if (!TryReadAt(stream, resourceBase + entriesOffset, buffer))
		{
			return null;
		}

		var entries = new List<ResourceEntry>(total);
		for (var i = 0; i < total; i++)
		{
			var span = buffer.AsSpan(i * ResourceDirectoryEntrySize);
			var nameOrId = BinaryPrimitives.ReadUInt32LittleEndian(span);
			var offsetToData = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
			entries.Add(new ResourceEntry(nameOrId & ~ResourceHighBit,
				(nameOrId & ResourceHighBit) is not 0,
				offsetToData & ~ResourceHighBit,
				(offsetToData & ResourceHighBit) is not 0));
		}

		return entries;
	}

	private static ResourceEntry? FindTypeDirectory(List<ResourceEntry> types, uint typeId)
	{
		foreach (var type in types)
		{
			if (!type.IsNamed && type.IsDirectory && type.Id == typeId)
			{
				return type;
			}
		}

		return null;
	}

	private static ResourceEntry? FindById(List<ResourceEntry> entries, uint id)
	{
		foreach (var entry in entries)
		{
			if (!entry.IsNamed && entry.Id == id)
			{
				return entry;
			}
		}

		return null;
	}

	private static ResourceEntry? SelectFirstLanguage(List<ResourceEntry> languages)
	{
		ResourceEntry? best = null;
		foreach (var language in languages)
		{
			if (language.IsNamed)
			{
				continue;
			}

			if (best is null || language.Id < best.Value.Id)
			{
				best = language;
			}
		}

		return best ?? (languages.Count is 0 ? null : languages[0]);
	}

	private static bool TryResolveLeaf(Stream stream,
		PeLayout layout,
		long resourceBase,
		long resourceSize,
		ResourceEntry entry,
		out long offset,
		out uint size)
	{
		offset = -1;
		size = 0;

		var leaf = entry;
		if (entry.IsDirectory)
		{
			var languages = TryReadDirectory(stream, resourceBase, resourceSize, entry.Offset);
			if (languages is null)
			{
				return false;
			}

			var selected = SelectFirstLanguage(languages);
			if (selected is null || selected.Value.IsDirectory)
			{
				return false;
			}

			leaf = selected.Value;
		}

		if (leaf.Offset + ResourceDataEntrySize > resourceSize)
		{
			return false;
		}

		Span<byte> dataEntry = stackalloc byte[ResourceDataEntrySize];
		if (!TryReadAt(stream, resourceBase + leaf.Offset, dataEntry))
		{
			return false;
		}

		var payloadRva = BinaryPrimitives.ReadUInt32LittleEndian(dataEntry);
		var payloadSize = BinaryPrimitives.ReadUInt32LittleEndian(dataEntry[4..]);
		if (payloadSize is 0 || payloadSize > MaxPayloadBytes)
		{
			return false;
		}

		if (!TryResolveRva(layout, payloadRva, out var payloadOffset, out var availableBytes) ||
			availableBytes < payloadSize)
		{
			return false;
		}

		offset = payloadOffset;
		size = payloadSize;
		return true;
	}

	private static byte[]? TryExtractFromGroup(Stream stream,
		PeLayout layout,
		long resourceBase,
		long resourceSize,
		List<ResourceEntry> types,
		List<ResourceEntry> icons)
	{
		var groupType = FindTypeDirectory(types, RtGroupIcon);
		if (groupType is null)
		{
			return null;
		}

		var groups = TryReadDirectory(stream, resourceBase, resourceSize, groupType.Value.Offset);
		if (groups is null)
		{
			return null;
		}

		foreach (var group in groups)
		{
			if (!TryResolveLeaf(stream, layout, resourceBase, resourceSize, group, out var offset, out var size))
			{
				continue;
			}

			var groupBytes = TryReadBytes(stream, offset, size);
			if (groupBytes is null)
			{
				continue;
			}

			var payload = TryReadBestGroupMember(stream, layout, resourceBase, resourceSize, icons, groupBytes);
			if (payload is not null)
			{
				return payload;
			}
		}

		return null;
	}

	private static byte[]? TryReadBestGroupMember(Stream stream,
		PeLayout layout,
		long resourceBase,
		long resourceSize,
		List<ResourceEntry> icons,
		byte[] groupBytes)
	{
		var candidates = ParseGroupDirectory(groupBytes);
		candidates.Sort(static (left, right) =>
		{
			var byArea = right.Area.CompareTo(left.Area);
			if (byArea is not 0)
			{
				return byArea;
			}

			var byBitCount = right.BitCount.CompareTo(left.BitCount);
			return byBitCount is not 0 ? byBitCount : left.Id.CompareTo(right.Id);
		});

		foreach (var candidate in candidates)
		{
			var icon = FindById(icons, candidate.Id);
			if (icon is null)
			{
				continue;
			}

			if (!TryResolveLeaf(stream, layout, resourceBase, resourceSize, icon.Value, out var offset, out var size))
			{
				continue;
			}

			var payload = TryReadBytes(stream, offset, size);
			if (payload is not null)
			{
				return payload;
			}
		}

		return null;
	}

	private static List<GroupIconEntry> ParseGroupDirectory(byte[] groupBytes)
	{
		var entries = new List<GroupIconEntry>();
		if (groupBytes.Length < GroupHeaderSize)
		{
			return entries;
		}

		var span = groupBytes.AsSpan();
		var count = BinaryPrimitives.ReadUInt16LittleEndian(span[4..]);
		count = (ushort)Math.Min((int)count, MaxGroupEntries);
		for (var i = 0; i < count; i++)
		{
			var entryOffset = GroupHeaderSize + (i * GroupEntrySize);
			if (entryOffset + GroupEntrySize > groupBytes.Length)
			{
				break;
			}

			var entry = span.Slice(entryOffset, GroupEntrySize);
			entries.Add(new GroupIconEntry(DimensionOf(entry[0]),
				DimensionOf(entry[1]),
				BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]),
				BinaryPrimitives.ReadUInt16LittleEndian(entry[12..])));
		}

		return entries;
	}

	private static byte[]? TryExtractLargestIcon(Stream stream,
		PeLayout layout,
		long resourceBase,
		long resourceSize,
		List<ResourceEntry> icons)
	{
		long bestArea = -1;
		var bestBitCount = -1;
		long bestOffset = -1;
		uint bestSize = 0;

		foreach (var icon in icons)
		{
			if (!TryResolveLeaf(stream, layout, resourceBase, resourceSize, icon, out var offset, out var size))
			{
				continue;
			}

			if (!TryMeasurePayload(stream, offset, size, out var width, out var height, out var bitCount))
			{
				continue;
			}

			var area = (long)width * height;
			if (area < bestArea || (area == bestArea && bitCount <= bestBitCount))
			{
				continue;
			}

			bestArea = area;
			bestBitCount = bitCount;
			bestOffset = offset;
			bestSize = size;
		}

		return bestOffset < 0 ? null : TryReadBytes(stream, bestOffset, bestSize);
	}

	private static bool TryMeasurePayload(Stream stream,
		long offset,
		uint size,
		out int width,
		out int height,
		out int bitCount)
	{
		width = 0;
		height = 0;
		bitCount = 0;

		Span<byte> buffer = stackalloc byte[BitmapInfoHeaderSize];
		var wanted = (int)Math.Min(size, (uint)buffer.Length);
		if (wanted < PngHeaderSize)
		{
			return false;
		}

		var header = buffer[..wanted];
		if (!TryReadAt(stream, offset, header))
		{
			return false;
		}

		if (StartsWithPngSignature(header))
		{
			width = (int)BinaryPrimitives.ReadUInt32BigEndian(header[16..]);
			height = (int)BinaryPrimitives.ReadUInt32BigEndian(header[20..]);
			bitCount = PngBitCount;
			return IsPlausible(width, height);
		}

		return TryReadDibHeader(header, out width, out height, out bitCount);
	}

	private static bool TryReadDibHeader(ReadOnlySpan<byte> payload, out int width, out int height, out int bitCount)
	{
		width = 0;
		height = 0;
		bitCount = 0;
		if (payload.Length < BitmapInfoHeaderSize)
		{
			return false;
		}

		if (BinaryPrimitives.ReadUInt32LittleEndian(payload) < BitmapInfoHeaderSize)
		{
			return false;
		}

		width = BinaryPrimitives.ReadInt32LittleEndian(payload[4..]);

		var storedHeight = (long)BinaryPrimitives.ReadInt32LittleEndian(payload[8..]);
		height = (int)(Math.Abs(storedHeight) / 2);
		bitCount = BinaryPrimitives.ReadUInt16LittleEndian(payload[14..]);
		return IsPlausible(width, height);
	}

	private static byte[]? TryWrapDibInIcon(byte[] payload)
	{
		if (!TryReadDibHeader(payload, out var width, out var height, out var bitCount))
		{
			return null;
		}

		var planes = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(12));
		var result = new byte[IconDirSize + IconDirEntrySize + payload.Length];
		var span = result.AsSpan();
		BinaryPrimitives.WriteUInt16LittleEndian(span[2..], 1);
		BinaryPrimitives.WriteUInt16LittleEndian(span[4..], 1);
		result[IconDirSize] = SizeByteOf(width);
		result[IconDirSize + 1] = SizeByteOf(height);
		result[IconDirSize + 2] = bitCount < 8 ? (byte)(1 << bitCount) : (byte)0;
		BinaryPrimitives.WriteUInt16LittleEndian(span[(IconDirSize + 4)..], planes);
		BinaryPrimitives.WriteUInt16LittleEndian(span[(IconDirSize + 6)..], (ushort)bitCount);
		BinaryPrimitives.WriteUInt32LittleEndian(span[(IconDirSize + 8)..], (uint)payload.Length);
		BinaryPrimitives.WriteUInt32LittleEndian(span[(IconDirSize + 12)..], IconDirSize + IconDirEntrySize);
		payload.CopyTo(span[(IconDirSize + IconDirEntrySize)..]);
		return result;
	}

	private static byte[]? TryReadBytes(Stream stream, long offset, uint size)
	{
		if (size is 0 || size > MaxPayloadBytes)
		{
			return null;
		}

		var buffer = new byte[size];
		return TryReadAt(stream, offset, buffer) ? buffer : null;
	}

	private static bool TryReadAt(Stream stream, long offset, Span<byte> buffer)
	{
		if (offset < 0 || buffer.Length > stream.Length - offset)
		{
			return false;
		}

		stream.Seek(offset, SeekOrigin.Begin);
		stream.ReadExactly(buffer);
		return true;
	}

	private static bool StartsWithPngSignature(ReadOnlySpan<byte> payload)
		=> payload.Length >= PngSignature.Length && payload[..PngSignature.Length].SequenceEqual(PngSignature);

	private static int DimensionOf(byte value) => value is 0 ? 256 : value;

	private static byte SizeByteOf(int dimension) => dimension >= 256 ? (byte)0 : (byte)dimension;

	private static bool IsPlausible(int width, int height)
		=> width > 0 && height > 0 && width <= MaxIconDimension && height <= MaxIconDimension;

	private sealed record PeLayout(Section[] Sections, uint ResourceRva, uint ResourceSize);

	private readonly record struct Section(
		uint VirtualAddress,
		uint VirtualSize,
		uint SizeOfRawData,
		uint PointerToRawData);

	private readonly record struct ResourceEntry(uint Id, bool IsNamed, uint Offset, bool IsDirectory);

	private readonly record struct GroupIconEntry(int Width, int Height, ushort BitCount, uint Id)
	{
		public long Area => (long)Width * Height;
	}
}
