using System.Buffers.Binary;
using System.Text;
using MacroDeckHost.Infrastructure.Icons.AppIcons;

namespace MacroDeckHost.Tests.UnitTests.Icons.AppIcons;

[TestFixture]
public class WindowsShellLinkParserTests
{
	private const string IconRootVariable = "MACRO_DECK_TEST_ICON_ROOT";
	private const string IconRootValue = @"C:\TestIcons";

	private const int ShellLinkHeaderSize = 0x4C;
	private const int LinkFlagsOffset = 0x14;
	private const int VolumeIdSize = 20;

	private const uint HasLinkTargetIdListFlag = 0x01;
	private const uint HasLinkInfoFlag = 0x02;
	private const uint HasNameFlag = 0x04;
	private const uint HasRelativePathFlag = 0x08;
	private const uint HasWorkingDirFlag = 0x10;
	private const uint HasArgumentsFlag = 0x20;
	private const uint HasIconLocationFlag = 0x40;
	private const uint IsUnicodeFlag = 0x80;
	private const uint VolumeIdAndLocalBasePathFlag = 0x01;

	private static readonly byte[] _validClsid =
	[
		0x01, 0x14, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00,
		0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46
	];

	[SetUp]
	public void SetUp()
	{
		Environment.SetEnvironmentVariable(IconRootVariable, IconRootValue);
	}

	[TearDown]
	public void TearDown()
	{
		Environment.SetEnvironmentVariable(IconRootVariable, null);
	}

	[Test]
	public void TryParse_UnicodeStringData_ReadsTargetPathAndIconLocation()
	{
		var bytes = new ShellLinkBuilder
		{
			UnicodeStringData = true,
			LocalBasePath = @"C:\Program Files\Example\example.exe",
			Name = "Example",
			RelativePath = @"..\example.exe",
			WorkingDir = @"C:\Program Files\Example",
			Arguments = "--minimized",
			IconLocation = @"C:\Program Files\Example\example.ico"
		}.Build();

		var result = WindowsShellLinkParser.TryParse(bytes);

		Assert.That(result, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(result!.TargetPath, Is.EqualTo(@"C:\Program Files\Example\example.exe"));
			Assert.That(result!.IconLocation, Is.EqualTo(@"C:\Program Files\Example\example.ico"));
		});
	}

	[Test]
	public void TryParse_AnsiStringData_ReadsIconLocation()
	{
		var bytes = new ShellLinkBuilder
		{
			LocalBasePath = @"C:\Tools\tool.exe",
			Name = "Tool",
			WorkingDir = @"C:\Tools",
			IconLocation = @"C:\Tools\tool.ico"
		}.Build();

		var result = WindowsShellLinkParser.TryParse(bytes);

		Assert.That(result, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(result!.TargetPath, Is.EqualTo(@"C:\Tools\tool.exe"));
			Assert.That(result!.IconLocation, Is.EqualTo(@"C:\Tools\tool.ico"));
		});
	}

	[Test]
	public void TryParse_NoIconLocation_ReadsTargetPathOnly()
	{
		var bytes = new ShellLinkBuilder
		{
			UnicodeStringData = true,
			LocalBasePath = @"C:\Tools\tool.exe",
			Name = "Tool",
			RelativePath = @"..\tool.exe"
		}.Build();

		var result = WindowsShellLinkParser.TryParse(bytes);

		Assert.That(result, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(result!.TargetPath, Is.EqualTo(@"C:\Tools\tool.exe"));
			Assert.That(result!.IconLocation, Is.Null);
		});
	}

	[Test]
	public void TryParse_LocalBasePathWithCommonPathSuffix_ConcatenatesBoth()
	{
		var bytes = new ShellLinkBuilder
		{
			LocalBasePath = @"C:\Program Files\",
			CommonPathSuffix = @"Example\example.exe",
			IconLocation = @"C:\Program Files\Example\example.exe"
		}.Build();

		var result = WindowsShellLinkParser.TryParse(bytes);

		Assert.That(result!.TargetPath, Is.EqualTo(@"C:\Program Files\Example\example.exe"));
	}

	[Test]
	public void TryParse_UnicodeLinkInfoOffsets_PreferredOverAnsiOffsets()
	{
		var bytes = new ShellLinkBuilder
		{
			UnicodeLinkInfo = true,
			LocalBasePath = @"C:\Spiele\Würfel\würfel.exe",
			AnsiLocalBasePath = @"C:\Spiele\W?rfel\w?rfel.exe",
			CommonPathSuffix = string.Empty
		}.Build();

		var result = WindowsShellLinkParser.TryParse(bytes);

		Assert.That(result!.TargetPath, Is.EqualTo(@"C:\Spiele\Würfel\würfel.exe"));
	}

	[Test]
	public void TryParse_LinkTargetIdListPresent_SkipsItAndStillReadsStringData()
	{
		var bytes = new ShellLinkBuilder
		{
			UnicodeStringData = true,
			IdList = [0x14, 0x00, 0x1F, 0x50, 0xE0, 0x4F, 0xD0, 0x20, 0xEA, 0x3A, 0x69, 0x10, 0xA2, 0xD8],
			LocalBasePath = @"C:\Tools\tool.exe",
			Name = "Tool",
			IconLocation = @"C:\Tools\tool.ico"
		}.Build();

		var result = WindowsShellLinkParser.TryParse(bytes);

		Assert.That(result, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(result!.TargetPath, Is.EqualTo(@"C:\Tools\tool.exe"));
			Assert.That(result!.IconLocation, Is.EqualTo(@"C:\Tools\tool.ico"));
		});
	}

	[Test]
	public void TryParse_LinkTargetIdListOnly_ReturnsLinkWithoutPaths()
	{
		var bytes = new ShellLinkBuilder { IdList = [0x04, 0x00, 0x00, 0x00] }.Build();

		var result = WindowsShellLinkParser.TryParse(bytes);

		Assert.That(result, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(result!.TargetPath, Is.Null);
			Assert.That(result!.IconLocation, Is.Null);
		});
	}

	[TestCase(@"C:\Windows\system32\shell32.dll,0", @"C:\Windows\system32\shell32.dll")]
	[TestCase(@"C:\Windows\system32\shell32.dll,-16", @"C:\Windows\system32\shell32.dll")]
	[TestCase(@"C:\My, Icons\icon.ico", @"C:\My, Icons\icon.ico")]
	public void TryParse_IconLocationWithIndex_TrimsOnlyANumericIndex(string stored, string expected)
	{
		var bytes = new ShellLinkBuilder { UnicodeStringData = true, IconLocation = stored }.Build();

		var result = WindowsShellLinkParser.TryParse(bytes);

		Assert.That(result!.IconLocation, Is.EqualTo(expected));
	}

	[Test]
	public void TryParse_IconLocationWithEnvironmentVariable_ReturnsExpandedPath()
	{
		var bytes = new ShellLinkBuilder
		{
			UnicodeStringData = true,
			IconLocation = $@"%{IconRootVariable}%\example.ico,0"
		}.Build();

		var result = WindowsShellLinkParser.TryParse(bytes);

		Assert.That(result!.IconLocation, Is.EqualTo($@"{IconRootValue}\example.ico"));
	}

	[Test]
	public void TryParse_EmptyIconLocation_ReturnsNullIconLocation()
	{
		var bytes = new ShellLinkBuilder { UnicodeStringData = true, IconLocation = string.Empty }.Build();

		var result = WindowsShellLinkParser.TryParse(bytes);

		Assert.That(result, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(result!.IconLocation, Is.Null);
			Assert.That(result!.TargetPath, Is.Null);
		});
	}

	[Test]
	public void TryParse_WrongClsid_ReturnsNull()
	{
		var clsid = (byte[])_validClsid.Clone();
		clsid[0] = 0x02;
		var bytes = new ShellLinkBuilder { Clsid = clsid, IconLocation = @"C:\Tools\tool.ico" }.Build();

		Assert.That(WindowsShellLinkParser.TryParse(bytes), Is.Null);
	}

	[Test]
	public void TryParse_WrongHeaderSize_ReturnsNull()
	{
		var bytes = new ShellLinkBuilder { HeaderSize = 0x50, IconLocation = @"C:\Tools\tool.ico" }.Build();

		Assert.That(WindowsShellLinkParser.TryParse(bytes), Is.Null);
	}

	[Test]
	public void TryParse_EmptyBuffer_ReturnsNull()
	{
		Assert.That(WindowsShellLinkParser.TryParse([]), Is.Null);
	}

	[TestCase(ShellLinkHeaderSize - 1)]
	[TestCase(ShellLinkHeaderSize)]
	[TestCase(ShellLinkHeaderSize + 4)]
	[TestCase(ShellLinkHeaderSize + 40)]
	public void TryParse_TruncatedBuffer_ReturnsNull(int length)
	{
		var bytes = new ShellLinkBuilder
		{
			UnicodeStringData = true,
			LocalBasePath = @"C:\Tools\tool.exe",
			Name = "Tool",
			IconLocation = @"C:\Tools\tool.ico"
		}.Build();

		Assert.That(WindowsShellLinkParser.TryParse(bytes[..length]), Is.Null);
	}

	[Test]
	public void TryParse_StringDataTruncatedMidCharacter_ReturnsNull()
	{
		var bytes = new ShellLinkBuilder
		{
			UnicodeStringData = true,
			IconLocation = @"C:\Tools\tool.ico"
		}.Build();

		Assert.That(WindowsShellLinkParser.TryParse(bytes[..^4]), Is.Null);
	}

	[Test]
	public void TryParse_LinkInfoSizeBeyondBuffer_ReturnsNull()
	{
		var bytes = new ShellLinkBuilder
		{
			LocalBasePath = @"C:\Tools\tool.exe",
			IconLocation = @"C:\Tools\tool.ico"
		}.Build();
		BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(ShellLinkHeaderSize), 0xFFFF);

		Assert.That(WindowsShellLinkParser.TryParse(bytes), Is.Null);
	}

	[Test]
	public void TryParse_LinkInfoSizeSmallerThanItsHeader_ReturnsNull()
	{
		var bytes = new ShellLinkBuilder { LocalBasePath = @"C:\Tools\tool.exe" }.Build();
		BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(ShellLinkHeaderSize), 0x10);

		Assert.That(WindowsShellLinkParser.TryParse(bytes), Is.Null);
	}

	[Test]
	public void TryParse_IdListSizeBeyondBuffer_ReturnsNull()
	{
		var bytes = new ShellLinkBuilder { IdList = [0x04, 0x00, 0x00, 0x00] }.Build();
		BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(ShellLinkHeaderSize), 0x7FFF);

		Assert.That(WindowsShellLinkParser.TryParse(bytes), Is.Null);
	}

	[Test]
	public void TryParse_FlagsSetWithoutTheirStringData_ReturnsNull()
	{
		var bytes = new ShellLinkBuilder { UnicodeStringData = true, IconLocation = @"C:\Tools\tool.ico" }.Build();
		var flags = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(LinkFlagsOffset));
		BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(LinkFlagsOffset), flags | HasArgumentsFlag);

		Assert.That(WindowsShellLinkParser.TryParse(bytes), Is.Null);
	}

	private sealed class ShellLinkBuilder
	{
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
			var flags = (IdList is not null ? HasLinkTargetIdListFlag : 0u) |
				(LocalBasePath is not null ? HasLinkInfoFlag : 0u) |
				(Name is not null ? HasNameFlag : 0u) |
				(RelativePath is not null ? HasRelativePathFlag : 0u) |
				(WorkingDir is not null ? HasWorkingDirFlag : 0u) |
				(Arguments is not null ? HasArgumentsFlag : 0u) |
				(IconLocation is not null ? HasIconLocationFlag : 0u) |
				(UnicodeStringData ? IsUnicodeFlag : 0u);

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
}
