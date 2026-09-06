using MacroDeckHost.Integrations.System.DesktopEntries;

namespace MacroDeckHost.Tests.UnitTests.System.DesktopEntries;

[TestFixture]
public class DesktopEntryParserTests
{
	[Test]
	public void TryReadIconName_IconInDesktopEntryGroup_ReturnsValue()
	{
		var icon = DesktopEntryParser.TryReadIconName([
			"[Desktop Entry]",
			"Type=Application",
			"Name=Firefox",
			"Icon=firefox",
			"Exec=firefox %u"
		]);

		Assert.That(icon, Is.EqualTo("firefox"));
	}

	[Test]
	public void TryReadIconName_CommentsBlankLinesAndSpacesAroundEquals_AreTolerated()
	{
		var icon = DesktopEntryParser.TryReadIconName([
			"# a comment",
			"",
			"  [Desktop Entry]  ",
			"   # Icon=commented-out",
			"  Icon =  gimp  ",
			""
		]);

		Assert.That(icon, Is.EqualTo("gimp"));
	}

	[Test]
	public void TryReadIconName_IconOnlyInAnotherGroup_ReturnsNull()
	{
		var icon = DesktopEntryParser.TryReadIconName([
			"[Desktop Entry]",
			"Name=Firefox",
			"[Desktop Action new-window]",
			"Icon=firefox-new-window"
		]);

		Assert.That(icon, Is.Null);
	}

	[Test]
	public void TryReadIconName_ActionGroupBeforeDesktopEntry_ReadsTheDesktopEntryValue()
	{
		var icon = DesktopEntryParser.TryReadIconName([
			"[Desktop Action new-window]",
			"Icon=firefox-new-window",
			"[Desktop Entry]",
			"Icon=firefox"
		]);

		Assert.That(icon, Is.EqualTo("firefox"));
	}

	[Test]
	public void TryReadIconName_LocalizedAndDifferentlyCasedKeys_AreIgnored()
	{
		var icon = DesktopEntryParser.TryReadIconName([
			"[Desktop Entry]",
			"Icon[de]=lokal",
			"icon=lowercase",
			"IconName=other",
			"Icon=chromium"
		]);

		Assert.That(icon, Is.EqualTo("chromium"));
	}

	[Test]
	public void TryReadIconName_DuplicateIconKeys_TakesTheFirst()
	{
		var icon = DesktopEntryParser.TryReadIconName(["[Desktop Entry]", "Icon=first", "Icon=second"]);

		Assert.That(icon, Is.EqualTo("first"));
	}

	[Test]
	public void TryReadIconName_EmptyValue_ReturnsNull()
	{
		var icon = DesktopEntryParser.TryReadIconName(["[Desktop Entry]", "Icon=   "]);

		Assert.That(icon, Is.Null);
	}

	[Test]
	public void TryReadIconName_AbsolutePathValue_IsReturnedVerbatim()
	{
		var icon = DesktopEntryParser.TryReadIconName(["[Desktop Entry]", "Icon=/opt/app/share/logo.png"]);

		Assert.That(icon, Is.EqualTo("/opt/app/share/logo.png"));
	}

	[Test]
	public void TryReadIconName_NoGroupHeaderOrKey_ReturnsNull()
	{
		Assert.Multiple(() =>
		{
			Assert.That(DesktopEntryParser.TryReadIconName(["Icon=orphan"]), Is.Null);
			Assert.That(DesktopEntryParser.TryReadIconName(["[Desktop Entry]", "Name=No icon"]), Is.Null);
			Assert.That(DesktopEntryParser.TryReadIconName([]), Is.Null);
			Assert.That(DesktopEntryParser.TryReadIconName(["[Desktop Entry]", "not a pair"]), Is.Null);
		});
	}

	[Test]
	public void TryReadCommand_SplitsTheProgramFromItsArguments()
	{
		var command = DesktopEntryParser.TryReadCommand([
			"[Desktop Entry]",
			"Exec=/usr/bin/firefox --new-window"
		]);

		Assert.Multiple(() =>
		{
			Assert.That(command!.Program, Is.EqualTo("/usr/bin/firefox"));
			Assert.That(command.Arguments, Is.EqualTo("--new-window"));
		});
	}

	[Test]
	public void TryReadCommand_DropsFieldCodesButKeepsAnEscapedPercent()
	{
		var command = DesktopEntryParser.TryReadCommand([
			"[Desktop Entry]",
			"Exec=gimp-2.10 %U --batch 100%%"
		]);

		Assert.Multiple(() =>
		{
			Assert.That(command!.Program, Is.EqualTo("gimp-2.10"));
			Assert.That(command.Arguments, Is.EqualTo("--batch 100%"));
		});
	}

	[Test]
	public void TryReadCommand_KeepsAQuotedPathWhole()
	{
		var command = DesktopEntryParser.TryReadCommand([
			"[Desktop Entry]",
			"Exec=\"/opt/My App/run\" \"a b\" %f"
		]);

		Assert.Multiple(() =>
		{
			Assert.That(command!.Program, Is.EqualTo("/opt/My App/run"));
			Assert.That(command.Arguments, Is.EqualTo("\"a b\""));
		});
	}

	[Test]
	public void TryReadCommand_ProgramOnly_HasNoArguments()
	{
		var command = DesktopEntryParser.TryReadCommand(["[Desktop Entry]", "Exec=firefox %u"]);

		Assert.Multiple(() =>
		{
			Assert.That(command!.Program, Is.EqualTo("firefox"));
			Assert.That(command.Arguments, Is.Null);
		});
	}

	[Test]
	public void TryReadCommand_NoExec_ReturnsNull()
	{
		Assert.Multiple(() =>
		{
			Assert.That(DesktopEntryParser.TryReadCommand(["[Desktop Entry]", "Name=No exec"]), Is.Null);
			Assert.That(DesktopEntryParser.TryReadCommand(["[Desktop Action New]", "Exec=other"]), Is.Null);
		});
	}

	[Test]
	public void TryReadName_ReturnsTheDisplayName()
	{
		Assert.Multiple(() =>
		{
			Assert.That(DesktopEntryParser.TryReadName(["[Desktop Entry]", "Name=Firefox"]), Is.EqualTo("Firefox"));
			// A localized key is a different key per spec, so it must not answer for the plain one.
			Assert.That(DesktopEntryParser.TryReadName(["[Desktop Entry]", "Name[de]=Fuchs"]), Is.Null);
		});
	}
}
