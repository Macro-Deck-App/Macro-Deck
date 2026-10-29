using System.Text;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal sealed class ShellLinkBuilder
{
	public const int ShellLinkHeaderSize = 0x4C;
	public const int LinkFlagsOffset = 0x14;
	public const uint HasArgumentsFlag = 0x20;

	private const int VolumeIdSize = 20;

	private const uint HasLinkTargetIdListFlag = 0x01;
	private const uint HasLinkInfoFlag = 0x02;
	private const uint HasNameFlag = 0x04;
	private const uint HasRelativePathFlag = 0x08;
	private const uint HasWorkingDirFlag = 0x10;
	private const uint HasIconLocationFlag = 0x40;
	private const uint IsUnicodeFlag = 0x80;
	private const uint VolumeIdAndLocalBasePathFlag = 0x01;

	private static readonly byte[] _validClsid =
	[
		0x01, 0x14, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00,
		0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46
	];

	public uint HeaderSize { get; init; } = ShellLinkHeaderSize;

	public byte[] Clsid { get; init; } = _validClsid;

	public byte[]? IdList { get; init; }

	public string? LocalBasePath { get; init; }

	public string? AnsiLocalBasePath { get; init; }

	public string? CommonPathSuffix { get; init; }

	public bool UnicodeLinkInfo { get; init; }

	public bool UnicodeStringData { get; init; }

	public string? Name { get; init; }

	public string? RelativePath { get; init; }

	public string? WorkingDir { get; init; }

	public string? Arguments { get; init; }

	public string? IconLocation { get; init; }

	public byte[] Build()
	{
		var flags = (IdList is not null ? HasLinkTargetIdListFlag : 0u)
			| (LocalBasePath is not null ? HasLinkInfoFlag : 0u)
			| (Name is not null ? HasNameFlag : 0u)
			| (RelativePath is not null ? HasRelativePathFlag : 0u)
			| (WorkingDir is not null ? HasWorkingDirFlag : 0u)
			| (Arguments is not null ? HasArgumentsFlag : 0u)
			| (IconLocation is not null ? HasIconLocationFlag : 0u)
			| (UnicodeStringData ? IsUnicodeFlag : 0u);

		using var stream = new MemoryStream();
		using var writer = new BinaryWriter(stream, Encoding.Unicode);
		writer.Write(HeaderSize);
		writer.Write(Clsid);
		writer.Write(flags);
		writer.Write(new byte[ShellLinkHeaderSize - LinkFlagsOffset - sizeof(uint)]);

		if (IdList is not null)
		{
			writer.Write((ushort)IdList.Length);
			writer.Write(IdList);
		}

		if (LocalBasePath is not null)
		{
			writer.Write(BuildLinkInfo());
		}

		WriteStringData(writer, Name);
		WriteStringData(writer, RelativePath);
		WriteStringData(writer, WorkingDir);
		WriteStringData(writer, Arguments);
		WriteStringData(writer, IconLocation);
		writer.Flush();
		return stream.ToArray();
	}

	private byte[] BuildLinkInfo()
	{
		var headerSize = UnicodeLinkInfo ? 0x24 : 0x1C;
		var suffix = CommonPathSuffix ?? string.Empty;
		var ansiPath = Encoding.Latin1.GetBytes((AnsiLocalBasePath ?? LocalBasePath ?? string.Empty) + '\0');
		var ansiSuffix = Encoding.Latin1.GetBytes(suffix + '\0');
		var unicodePath = Encoding.Unicode.GetBytes((LocalBasePath ?? string.Empty) + '\0');
		var unicodeSuffix = Encoding.Unicode.GetBytes(suffix + '\0');

		var volumeIdOffset = headerSize;
		var ansiPathOffset = volumeIdOffset + VolumeIdSize;
		var ansiSuffixOffset = ansiPathOffset + ansiPath.Length;
		var unicodePathOffset = ansiSuffixOffset + ansiSuffix.Length;
		var unicodeSuffixOffset = unicodePathOffset + unicodePath.Length;
		var size = UnicodeLinkInfo ? unicodeSuffixOffset + unicodeSuffix.Length : unicodePathOffset;

		using var stream = new MemoryStream();
		using var writer = new BinaryWriter(stream);
		writer.Write((uint)size);
		writer.Write((uint)headerSize);
		writer.Write(VolumeIdAndLocalBasePathFlag);
		writer.Write((uint)volumeIdOffset);
		writer.Write((uint)ansiPathOffset);
		writer.Write(0u);
		writer.Write((uint)ansiSuffixOffset);
		if (UnicodeLinkInfo)
		{
			writer.Write((uint)unicodePathOffset);
			writer.Write((uint)unicodeSuffixOffset);
		}

		writer.Write(new byte[VolumeIdSize]);
		writer.Write(ansiPath);
		writer.Write(ansiSuffix);
		if (UnicodeLinkInfo)
		{
			writer.Write(unicodePath);
			writer.Write(unicodeSuffix);
		}

		writer.Flush();
		return stream.ToArray();
	}

	private void WriteStringData(BinaryWriter writer, string? value)
	{
		if (value is null)
		{
			return;
		}

		writer.Write((ushort)value.Length);
		writer.Write(UnicodeStringData ? Encoding.Unicode.GetBytes(value) : Encoding.Latin1.GetBytes(value));
	}
}
