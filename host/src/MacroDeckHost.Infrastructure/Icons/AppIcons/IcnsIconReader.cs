using System.Buffers.Binary;

namespace MacroDeckHost.Infrastructure.Icons.AppIcons;

internal static class IcnsIconReader
{
	private const int ChunkHeaderLength = 8;
	private const int PngIhdrWidthOffset = 16;
	private const int MinimumPngHeaderLength = 24;

	private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

	public static byte[]? TryExtractBestIcon(byte[] bytes)
	{
		if (bytes.Length < ChunkHeaderLength)
		{
			return null;
		}

		var span = new ReadOnlySpan<byte>(bytes);
		if (!span[..4].SequenceEqual("icns"u8))
		{
			return null;
		}

		var declared = BinaryPrimitives.ReadUInt32BigEndian(span[4..ChunkHeaderLength]);
		var containerLength = declared >= ChunkHeaderLength && declared <= (uint)bytes.Length
			? (int)declared
			: bytes.Length;

		byte[]? best = null;
		var bestWidth = 0;
		var bestLength = 0;
		var offset = ChunkHeaderLength;
		while (offset + ChunkHeaderLength <= containerLength)
		{
			var chunkType = span.Slice(offset, 4);
			var chunkLength = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(offset + 4, 4));
			if (chunkLength < ChunkHeaderLength || (long)offset + chunkLength > containerLength)
			{
				break;
			}

			var payload = span.Slice(offset + ChunkHeaderLength, (int)chunkLength - ChunkHeaderLength);
			if (!IsMetadataType(chunkType) && TryReadPngWidth(payload, out var width))
			{
				if (width > bestWidth || (width == bestWidth && payload.Length > bestLength))
				{
					best = payload.ToArray();
					bestWidth = width;
					bestLength = payload.Length;
				}
			}

			offset += (int)chunkLength;
		}

		return best;
	}

	private static bool IsMetadataType(ReadOnlySpan<byte> chunkType)
		=> chunkType.SequenceEqual("TOC "u8) ||
			chunkType.SequenceEqual("icnV"u8) ||
			chunkType.SequenceEqual("name"u8) ||
			chunkType.SequenceEqual("info"u8);

	private static bool TryReadPngWidth(ReadOnlySpan<byte> payload, out int width)
	{
		width = 0;
		if (payload.Length < MinimumPngHeaderLength || !payload[..PngSignature.Length].SequenceEqual(PngSignature))
		{
			return false;
		}

		var value = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(PngIhdrWidthOffset, 4));
		if (value == 0 || value > int.MaxValue)
		{
			return false;
		}

		width = (int)value;
		return true;
	}
}
