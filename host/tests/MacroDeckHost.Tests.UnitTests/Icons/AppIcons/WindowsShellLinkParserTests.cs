using System.Buffers.Binary;
using MacroDeckHost.Platform.ShellLinks;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Icons.AppIcons;

[TestFixture]
public class WindowsShellLinkParserTests
{
	private const string IconRootVariable = "MACRO_DECK_TEST_ICON_ROOT";
	private const string IconRootValue = @"C:\TestIcons";

	private const int ShellLinkHeaderSize = ShellLinkBuilder.ShellLinkHeaderSize;
	private const int LinkFlagsOffset = ShellLinkBuilder.LinkFlagsOffset;
	private const uint HasArgumentsFlag = ShellLinkBuilder.HasArgumentsFlag;

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
			Assert.That(result!.Arguments, Is.EqualTo("--minimized"));
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
}
