using System.Text;
using MacroDeckHost.Infrastructure.Icons.AppIcons;

namespace MacroDeckHost.Tests.UnitTests.Icons.AppIcons;

[TestFixture]
public class PropertyListIconParserTests
{
	private const string Doctype =
		"""
		<?xml version="1.0" encoding="UTF-8"?>
		<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
		""";

	[Test]
	public void TryReadIconFileName_XmlPlistWithIconFile_ReturnsDeclaredName()
	{
		var xml = Plist("<key>CFBundleName</key><string>Foo</string>" +
			"<key>CFBundleIconFile</key><string>AppIcon.icns</string>");

		Assert.That(PropertyListIconParser.TryReadIconFileName(xml), Is.EqualTo("AppIcon.icns"));
	}

	[Test]
	public void TryReadIconFileName_AppleDoctypeHeader_IsParsedWithoutResolvingTheDtd()
	{
		var xml = Doctype +
			Plist("<key>CFBundleIconFile</key><string>AppIcon</string>");

		Assert.That(PropertyListIconParser.TryReadIconFileName(xml), Is.EqualTo("AppIcon"));
	}

	[Test]
	public void TryReadIconFileName_OnlyIconName_FallsBackToIconName()
	{
		var xml = Plist("<key>CFBundleIconName</key><string>AssetIcon</string>");

		Assert.That(PropertyListIconParser.TryReadIconFileName(xml), Is.EqualTo("AssetIcon"));
	}

	[Test]
	public void TryReadIconFileName_BothKeys_PrefersIconFile()
	{
		var xml = Plist("<key>CFBundleIconName</key><string>AssetIcon</string>" +
			"<key>CFBundleIconFile</key><string>FileIcon.icns</string>");

		Assert.That(PropertyListIconParser.TryReadIconFileName(xml), Is.EqualTo("FileIcon.icns"));
	}

	[Test]
	public void TryReadIconFileName_ValueIsNotAString_IsIgnored()
	{
		var iconFileIsBool = Plist("<key>CFBundleIconFile</key><true/>");
		var iconFileIsArray = Plist("<key>CFBundleIconFile</key>" +
			"<array><string>AppIcon.icns</string></array>" +
			"<key>CFBundleIconName</key><string>AssetIcon</string>");

		Assert.Multiple(() =>
		{
			Assert.That(PropertyListIconParser.TryReadIconFileName(iconFileIsBool), Is.Null);
			Assert.That(PropertyListIconParser.TryReadIconFileName(iconFileIsArray), Is.EqualTo("AssetIcon"));
		});
	}

	[Test]
	public void TryReadIconFileName_NoIconKey_ReturnsNull()
	{
		var xml = Plist("<key>CFBundleName</key><string>Foo</string>");

		Assert.That(PropertyListIconParser.TryReadIconFileName(xml), Is.Null);
	}

	[Test]
	public void TryReadIconFileName_EmptyStringValue_ReturnsNull()
	{
		var xml = Plist("<key>CFBundleIconFile</key><string>   </string>");

		Assert.That(PropertyListIconParser.TryReadIconFileName(xml), Is.Null);
	}

	[Test]
	public void TryReadIconFileName_BinaryPlist_ReturnsNull()
	{
		var bytes = new byte[] { 0x62, 0x70, 0x6C, 0x69, 0x73, 0x74, 0x30, 0x30, 0xD1, 0x01, 0x02, 0x5F, 0x00, 0xFF };

		Assert.That(PropertyListIconParser.TryReadIconFileName(Encoding.UTF8.GetString(bytes)), Is.Null);
	}

	[Test]
	public void TryReadIconFileName_MalformedXml_ReturnsNull()
	{
		Assert.Multiple(() =>
		{
			Assert.That(PropertyListIconParser.TryReadIconFileName("<plist><dict><key>CFBundleIconFile"), Is.Null);
			Assert.That(PropertyListIconParser.TryReadIconFileName("not xml at all"), Is.Null);
			Assert.That(PropertyListIconParser.TryReadIconFileName(string.Empty), Is.Null);
		});
	}

	private static string Plist(string body)
		=> $"<plist version=\"1.0\"><dict>{body}</dict></plist>";
}
