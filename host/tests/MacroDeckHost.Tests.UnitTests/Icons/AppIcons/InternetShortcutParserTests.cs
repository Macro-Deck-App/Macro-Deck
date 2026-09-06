using MacroDeckHost.Infrastructure.Icons.AppIcons;

namespace MacroDeckHost.Tests.UnitTests.Icons.AppIcons;

[TestFixture]
public class InternetShortcutParserTests
{
	private const string IconRootVariable = "MACRO_DECK_TEST_URL_ICON_ROOT";
	private const string IconRootValue = @"C:\TestIcons";

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
	public void TryReadIconFile_IconFileInInternetShortcutSection_ReturnsPath()
	{
		var icon = InternetShortcutParser.TryReadIconFile([
			"[InternetShortcut]",
			"URL=https://example.com/",
			@"IconFile=C:\Icons\example.ico",
			"IconIndex=0"
		]);

		Assert.That(icon, Is.EqualTo(@"C:\Icons\example.ico"));
	}

	[Test]
	public void TryReadIconFile_CaseInsensitiveSectionAndKey_ReturnsPath()
	{
		var icon = InternetShortcutParser.TryReadIconFile([
			"[internetSHORTCUT]",
			@"  iconfile = C:\Icons\example.ico  "
		]);

		Assert.That(icon, Is.EqualTo(@"C:\Icons\example.ico"));
	}

	[Test]
	public void TryReadIconFile_IgnoresBlankLinesAndComments()
	{
		var icon = InternetShortcutParser.TryReadIconFile([
			"",
			"; a comment",
			"[InternetShortcut]",
			"   ",
			@"; IconFile=C:\Icons\commented.ico",
			"not a pair",
			@"IconFile=C:\Icons\example.ico"
		]);

		Assert.That(icon, Is.EqualTo(@"C:\Icons\example.ico"));
	}

	[Test]
	public void TryReadIconFile_IconFileInOtherSection_ReturnsNull()
	{
		var icon = InternetShortcutParser.TryReadIconFile([
			"[DEFAULT]",
			@"IconFile=C:\Icons\other.ico",
			"[InternetShortcut]",
			"URL=https://example.com/"
		]);

		Assert.That(icon, Is.Null);
	}

	[Test]
	public void TryReadIconFile_IconFileAfterSectionEnded_ReturnsNull()
	{
		var icon = InternetShortcutParser.TryReadIconFile([
			"[InternetShortcut]",
			"URL=https://example.com/",
			"[InternetShortcut.A]",
			@"IconFile=C:\Icons\other.ico"
		]);

		Assert.That(icon, Is.Null);
	}

	[Test]
	public void TryReadIconFile_NoIconFile_ReturnsNull()
	{
		var icon = InternetShortcutParser.TryReadIconFile([
			"[InternetShortcut]",
			"URL=https://example.com/",
			"IconIndex=0"
		]);

		Assert.That(icon, Is.Null);
	}

	[Test]
	public void TryReadIconFile_EmptyIconFileValue_ReturnsNull()
	{
		var icon = InternetShortcutParser.TryReadIconFile(["[InternetShortcut]", "IconFile=   "]);

		Assert.That(icon, Is.Null);
	}

	[Test]
	public void TryReadIconFile_SeveralIconFileEntries_ReturnsTheFirst()
	{
		var icon = InternetShortcutParser.TryReadIconFile([
			"[InternetShortcut]",
			@"IconFile=C:\Icons\first.ico",
			@"IconFile=C:\Icons\second.ico"
		]);

		Assert.That(icon, Is.EqualTo(@"C:\Icons\first.ico"));
	}

	[Test]
	public void TryReadIconFile_EnvironmentVariableInPath_ReturnsExpandedPath()
	{
		var icon = InternetShortcutParser.TryReadIconFile([
			"[InternetShortcut]",
			$@"IconFile=%{IconRootVariable}%\example.ico"
		]);

		Assert.That(icon, Is.EqualTo($@"{IconRootValue}\example.ico"));
	}

	[Test]
	public void TryReadIconFile_NoSectionHeader_ReturnsNull()
	{
		var icon = InternetShortcutParser.TryReadIconFile([@"IconFile=C:\Icons\example.ico"]);

		Assert.That(icon, Is.Null);
	}

	[Test]
	public void TryReadIconFile_NoLines_ReturnsNull()
	{
		Assert.That(InternetShortcutParser.TryReadIconFile([]), Is.Null);
	}
}
