using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace MacroDeckHost.Infrastructure.Icons.AppIcons;

internal sealed record ShellLinkTarget(string? TargetPath, string? IconLocation);

internal static class WindowsShellLinkParser
{
	private const int ShellLinkHeaderSize = 0x4C;
	private const int LinkFlagsOffset = 0x14;
	private const int ClsidOffset = 0x04;
	private const int ClsidLength = 16;

	private const int LinkInfoMinimumHeaderSize = 0x1C;
	private const int LinkInfoUnicodeHeaderSize = 0x24;
	private const int LinkInfoHeaderSizeField = 0x04;
	private const int LinkInfoFlagsField = 0x08;
	private const int LocalBasePathOffsetField = 0x10;
	private const int CommonPathSuffixOffsetField = 0x18;
	private const int LocalBasePathOffsetUnicodeField = 0x1C;
	private const int CommonPathSuffixOffsetUnicodeField = 0x20;

	private const uint HasLinkTargetIdList = 1u << 0;
	private const uint HasLinkInfo = 1u << 1;
	private const uint HasName = 1u << 2;
	private const uint HasRelativePath = 1u << 3;
	private const uint HasWorkingDir = 1u << 4;
	private const uint HasArguments = 1u << 5;
	private const uint HasIconLocation = 1u << 6;
	private const uint IsUnicode = 1u << 7;

	private const uint VolumeIdAndLocalBasePath = 1u << 0;

	private static readonly byte[] _linkClsid =
	[
		0x01, 0x14, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00,
		0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46
	];

	public static ShellLinkTarget? TryParse(byte[] bytes)
	{
		ReadOnlySpan<byte> span = bytes;
		if (span.Length < ShellLinkHeaderSize)
		{
			return null;
		}

		if (BinaryPrimitives.ReadUInt32LittleEndian(span) != ShellLinkHeaderSize)
		{
			return null;
		}

		if (!span.Slice(ClsidOffset, ClsidLength).SequenceEqual(_linkClsid))
		{
			return null;
		}

		var flags = BinaryPrimitives.ReadUInt32LittleEndian(span[LinkFlagsOffset..]);
		var offset = ShellLinkHeaderSize;

		if ((flags & HasLinkTargetIdList) != 0 && !TrySkipIdList(span, ref offset))
		{
			return null;
		}

		string? targetPath = null;
		if ((flags & HasLinkInfo) != 0 && !TryReadLinkInfo(span, ref offset, out targetPath))
		{
			return null;
		}

		var unicode = (flags & IsUnicode) != 0;
		ReadOnlySpan<uint> leadingStrings = [HasName, HasRelativePath, HasWorkingDir, HasArguments];
		foreach (var flag in leadingStrings)
		{
			if ((flags & flag) != 0 && !TryReadStringData(span, ref offset, unicode, out _))
			{
				return null;
			}
		}

		string? iconLocation = null;
		if ((flags & HasIconLocation) != 0 && !TryReadStringData(span, ref offset, unicode, out iconLocation))
		{
			return null;
		}

		return new ShellLinkTarget(NormalizePath(targetPath), NormalizeIconLocation(iconLocation));
	}

	private static bool TrySkipIdList(ReadOnlySpan<byte> span, ref int offset)
	{
		if (offset + sizeof(ushort) > span.Length)
		{
			return false;
		}

		var idListSize = BinaryPrimitives.ReadUInt16LittleEndian(span[offset..]);
		offset += sizeof(ushort) + idListSize;
		return offset <= span.Length;
	}

	private static bool TryReadLinkInfo(ReadOnlySpan<byte> span, ref int offset, out string? targetPath)
	{
		targetPath = null;
		if (offset + LinkInfoMinimumHeaderSize > span.Length)
		{
			return false;
		}

		var linkInfoSize = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
		if (linkInfoSize < LinkInfoMinimumHeaderSize || offset + linkInfoSize > (uint)span.Length)
		{
			return false;
		}

		var linkInfo = span.Slice(offset, (int)linkInfoSize);
		offset += (int)linkInfoSize;

		var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(linkInfo[LinkInfoHeaderSizeField..]);
		var linkInfoFlags = BinaryPrimitives.ReadUInt32LittleEndian(linkInfo[LinkInfoFlagsField..]);
		if ((linkInfoFlags & VolumeIdAndLocalBasePath) == 0)
		{
			return true;
		}

		if (headerSize >= LinkInfoUnicodeHeaderSize && linkInfoSize >= LinkInfoUnicodeHeaderSize)
		{
			var unicodePathOffset = ReadOffsetField(linkInfo, LocalBasePathOffsetUnicodeField);
			if (unicodePathOffset != 0)
			{
				var unicodeSuffixOffset = ReadOffsetField(linkInfo, CommonPathSuffixOffsetUnicodeField);
				var unicodePath = ReadUnicodeString(linkInfo, unicodePathOffset);
				var unicodeSuffix = ReadUnicodeString(linkInfo, unicodeSuffixOffset);
				targetPath = JoinPath(unicodePath, unicodeSuffix);
				return true;
			}
		}

		var path = ReadAnsiString(linkInfo, ReadOffsetField(linkInfo, LocalBasePathOffsetField));
		var suffix = ReadAnsiString(linkInfo, ReadOffsetField(linkInfo, CommonPathSuffixOffsetField));
		targetPath = JoinPath(path, suffix);
		return true;
	}

	private static bool TryReadStringData(ReadOnlySpan<byte> span, ref int offset, bool unicode, out string? value)
	{
		value = null;
		if (offset + sizeof(ushort) > span.Length)
		{
			return false;
		}

		var characters = BinaryPrimitives.ReadUInt16LittleEndian(span[offset..]);
		offset += sizeof(ushort);

		var byteCount = unicode ? characters * 2 : characters;
		if (offset + byteCount > span.Length)
		{
			return false;
		}

		var raw = span.Slice(offset, byteCount);
		offset += byteCount;
		value = unicode ? Encoding.Unicode.GetString(raw) : Encoding.Latin1.GetString(raw);
		return true;
	}

	private static uint ReadOffsetField(ReadOnlySpan<byte> linkInfo, int field)
		=> BinaryPrimitives.ReadUInt32LittleEndian(linkInfo[field..]);

	private static string? ReadAnsiString(ReadOnlySpan<byte> linkInfo, uint offset)
	{
		if (offset == 0 || offset >= (uint)linkInfo.Length)
		{
			return null;
		}

		var rest = linkInfo[(int)offset..];
		var terminator = rest.IndexOf((byte)0);
		return Encoding.Latin1.GetString(terminator < 0 ? rest : rest[..terminator]);
	}

	private static string? ReadUnicodeString(ReadOnlySpan<byte> linkInfo, uint offset)
	{
		if (offset == 0 || offset >= (uint)linkInfo.Length)
		{
			return null;
		}

		var rest = linkInfo[(int)offset..];
		if (rest.Length < sizeof(char))
		{
			return null;
		}

		for (var i = 0; i + 1 < rest.Length; i += 2)
		{
			if (rest[i] == 0 && rest[i + 1] == 0)
			{
				return Encoding.Unicode.GetString(rest[..i]);
			}
		}

		return Encoding.Unicode.GetString(rest[..(rest.Length & ~1)]);
	}

	private static string? JoinPath(string? basePath, string? commonPathSuffix)
	{
		if (string.IsNullOrEmpty(basePath))
		{
			return null;
		}

		return string.IsNullOrEmpty(commonPathSuffix) ? basePath : basePath + commonPathSuffix;
	}

	private static string? NormalizePath(string? value)
	{
		var trimmed = value?.Trim();
		return string.IsNullOrEmpty(trimmed) ? null : trimmed;
	}

	private static string? NormalizeIconLocation(string? value)
	{
		if (value is null)
		{
			return null;
		}

		var expanded = Environment.ExpandEnvironmentVariables(TrimIconIndex(value)).Trim();
		return expanded.Length == 0 ? null : expanded;
	}

	private static string TrimIconIndex(string value)
	{
		var separator = value.LastIndexOf(',');
		if (separator <= 0)
		{
			return value;
		}

		var index = value[(separator + 1)..].Trim();
		if (index.Length == 0)
		{
			return value[..separator];
		}

		return int.TryParse(index, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _)
			? value[..separator]
			: value;
	}
}
