using System.Buffers.Binary;
using SkiaSharp;

namespace MacroDeckHost.Infrastructure.Rendering;

internal static class SfntFaceExtractor
{
	private const uint HeadTag = 0x68656164;
	private const uint MaxpTag = 0x6D617870;
	private const uint CffTag = 0x43464620;
	private const uint Cff2Tag = 0x43464632;

	private const uint TrueTypeOutlineVersion = 0x00010000;
	private const uint CffOutlineVersion = 0x4F54544F;
	private const uint ChecksumMagic = 0xB1B0AFBA;

	private const int OffsetTableLength = 12;
	private const int DirectoryEntryLength = 16;
	private const int HeadCheckSumAdjustmentOffset = 8;
	private const int MinimumHeadLength = HeadCheckSumAdjustmentOffset + 4;

	// A face whose glyphs live in none of these cannot be rendered by any sfnt consumer we serve.
	// macOS 15 system faces such as PingFang carry their outlines in Apple's private 'hvgl' table.
	private static readonly uint[] GlyphSourceTags =
	[
		0x676C7966, // glyf
		CffTag,
		Cff2Tag,
		0x73626978, // sbix
		0x43424454, // CBDT
		0x45424454 // EBDT
	];

	public static bool CanExtract(SKTypeface typeface)
	{
		if (!TryGetTags(typeface, out var tags))
		{
			return false;
		}

		return Array.IndexOf(tags, MaxpTag) >= 0 &&
			Array.Exists(GlyphSourceTags, tag => Array.IndexOf(tags, tag) >= 0) &&
			typeface.GetTableSize(HeadTag) >= MinimumHeadLength;
	}

	public static byte[]? Extract(SKTypeface typeface)
	{
		if (!CanExtract(typeface) || !TryGetTags(typeface, out var tags))
		{
			return null;
		}

		var tables = new byte[tags.Length][];
		for (var i = 0; i < tags.Length; i++)
		{
			if (!typeface.TryGetTableData(tags[i], out var data) || data is null)
			{
				return null;
			}

			tables[i] = data;
		}

		var headIndex = Array.IndexOf(tags, HeadTag);
		if (tables[headIndex].Length < MinimumHeadLength)
		{
			return null;
		}

		// The stored head.checkSumAdjustment is only valid for the container the face came from.
		tables[headIndex].AsSpan(HeadCheckSumAdjustmentOffset, 4).Clear();

		return Assemble(tags, tables, headIndex);
	}

	private static byte[] Assemble(uint[] tags, byte[][] tables, int headIndex)
	{
		var directoryLength = OffsetTableLength + (tags.Length * DirectoryEntryLength);
		var total = directoryLength;
		foreach (var table in tables)
		{
			total += Aligned(table.Length);
		}

		var file = new byte[total];
		var span = file.AsSpan();

		var hasCffOutlines = Array.IndexOf(tags, CffTag) >= 0 || Array.IndexOf(tags, Cff2Tag) >= 0;
		BinaryPrimitives.WriteUInt32BigEndian(span, hasCffOutlines ? CffOutlineVersion : TrueTypeOutlineVersion);

		var entrySelector = 0;
		while (1 << (entrySelector + 1) <= tags.Length)
		{
			entrySelector++;
		}

		var searchRange = (1 << entrySelector) * DirectoryEntryLength;
		BinaryPrimitives.WriteUInt16BigEndian(span[4..], (ushort)tags.Length);
		BinaryPrimitives.WriteUInt16BigEndian(span[6..], (ushort)searchRange);
		BinaryPrimitives.WriteUInt16BigEndian(span[8..], (ushort)entrySelector);
		BinaryPrimitives.WriteUInt16BigEndian(span[10..], (ushort)((tags.Length * DirectoryEntryLength) - searchRange));

		var offset = directoryLength;
		for (var i = 0; i < tags.Length; i++)
		{
			tables[i].CopyTo(span[offset..]);

			var entry = OffsetTableLength + (i * DirectoryEntryLength);
			BinaryPrimitives.WriteUInt32BigEndian(span[entry..], tags[i]);
			BinaryPrimitives.WriteUInt32BigEndian(span[(entry + 4)..],
				Checksum(span.Slice(offset, Aligned(tables[i].Length))));
			BinaryPrimitives.WriteUInt32BigEndian(span[(entry + 8)..], (uint)offset);
			BinaryPrimitives.WriteUInt32BigEndian(span[(entry + 12)..], (uint)tables[i].Length);

			offset += Aligned(tables[i].Length);
		}

		var headOffset = directoryLength;
		for (var i = 0; i < headIndex; i++)
		{
			headOffset += Aligned(tables[i].Length);
		}

		BinaryPrimitives.WriteUInt32BigEndian(span[(headOffset + HeadCheckSumAdjustmentOffset)..],
			unchecked(ChecksumMagic - Checksum(span)));

		return file;
	}

	private static bool TryGetTags(SKTypeface typeface, out uint[] tags)
	{
		tags = [];
		if (!typeface.TryGetTableTags(out var raw) || raw is null || raw.Length == 0)
		{
			return false;
		}

		tags = [.. raw.Distinct().Order()];
		return tags.Length <= ushort.MaxValue && Array.IndexOf(tags, HeadTag) >= 0;
	}

	private static int Aligned(int length) => (length + 3) & ~3;

	private static uint Checksum(ReadOnlySpan<byte> data)
	{
		uint sum = 0;
		var i = 0;
		for (; i + 4 <= data.Length; i += 4)
		{
			sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(data[i..]));
		}

		if (i < data.Length)
		{
			uint tail = 0;
			for (var j = 0; j < 4; j++)
			{
				tail = (tail << 8) | (i + j < data.Length ? data[i + j] : 0u);
			}

			sum = unchecked(sum + tail);
		}

		return sum;
	}
}
