using System.Text;
using MacroDeckHost.Infrastructure.Icons.AppIcons;

namespace MacroDeckHost.Tests.UnitTests.Icons.AppIcons;

[TestFixture]
public class MacAppBundleIconLocatorTests
{
	private string _root = null!;
	private string _bundle = null!;
	private string _contents = null!;
	private string _resources = null!;

	[SetUp]
	public void SetUp()
	{
		_root = Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"));
		_bundle = Path.Combine(_root, "Foo.app");
		_contents = Path.Combine(_bundle, "Contents");
		_resources = Path.Combine(_contents, "Resources");
		Directory.CreateDirectory(_resources);
	}

	[TearDown]
	public void TearDown()
	{
		if (Directory.Exists(_root))
		{
			Directory.Delete(_root, true);
		}
	}

	[Test]
	public void IsAppBundle_AppPathWithAndWithoutTrailingSeparator_ReturnsTrue()
	{
		Assert.Multiple(() =>
		{
			Assert.That(MacAppBundleIconLocator.IsAppBundle("/Applications/Foo.app"), Is.True);
			Assert.That(MacAppBundleIconLocator.IsAppBundle("/Applications/Foo.app/"), Is.True);
			Assert.That(MacAppBundleIconLocator.IsAppBundle("/Applications/Foo.APP"), Is.True);
		});
	}

	[Test]
	public void IsAppBundle_PlainPath_ReturnsFalse()
	{
		Assert.Multiple(() =>
		{
			Assert.That(MacAppBundleIconLocator.IsAppBundle("/Applications/Foo"), Is.False);
			Assert.That(MacAppBundleIconLocator.IsAppBundle("/Applications/Foo.appx"), Is.False);
			Assert.That(MacAppBundleIconLocator.IsAppBundle(string.Empty), Is.False);
		});
	}

	[Test]
	public void TryResolveIconFile_PlistDeclaresIcon_PrefersItOverTheLargestIcns()
	{
		WriteBytes(Path.Combine(_resources, "Declared.icns"), 64);
		WriteBytes(Path.Combine(_resources, "Bigger.icns"), 4096);
		WritePlist("<key>CFBundleIconFile</key><string>Declared.icns</string>");

		var resolved = MacAppBundleIconLocator.TryResolveIconFile(_bundle);

		Assert.That(resolved, Is.EqualTo(Path.Combine(_resources, "Declared.icns")));
	}

	[Test]
	public void TryResolveIconFile_DeclaredNameOmitsExtension_AppendsIcns()
	{
		WriteBytes(Path.Combine(_resources, "AppIcon.icns"), 64);
		WriteBytes(Path.Combine(_resources, "Bigger.icns"), 4096);
		WritePlist("<key>CFBundleIconFile</key><string>AppIcon</string>");

		var resolved = MacAppBundleIconLocator.TryResolveIconFile(_bundle);

		Assert.That(resolved, Is.EqualTo(Path.Combine(_resources, "AppIcon.icns")));
	}

	[Test]
	public void TryResolveIconFile_DeclaredNameCaseMismatch_StillResolves()
	{
		WriteBytes(Path.Combine(_resources, "appicon.icns"), 64);
		WriteBytes(Path.Combine(_resources, "Bigger.icns"), 4096);
		WritePlist("<key>CFBundleIconFile</key><string>AppICON.icns</string>");

		var resolved = MacAppBundleIconLocator.TryResolveIconFile(_bundle);

		Assert.That(resolved, Is.EqualTo(Path.Combine(_resources, "appicon.icns")));
	}

	[Test]
	public void TryResolveIconFile_BinaryPlist_FallsBackToTheLargestIcns()
	{
		WriteBytes(Path.Combine(_resources, "Small.icns"), 64);
		WriteBytes(Path.Combine(_resources, "Large.icns"), 8192);
		WriteBytes(Path.Combine(_resources, "Huge.png"), 65536);
		File.WriteAllBytes(Path.Combine(_contents, "Info.plist"),
			[0x62, 0x70, 0x6C, 0x69, 0x73, 0x74, 0x30, 0x30, 0xD1, 0x00, 0xFF]);

		var resolved = MacAppBundleIconLocator.TryResolveIconFile(_bundle);

		Assert.That(resolved, Is.EqualTo(Path.Combine(_resources, "Large.icns")));
	}

	[Test]
	public void TryResolveIconFile_DeclaredFileMissing_FallsBackToTheLargestIcns()
	{
		WriteBytes(Path.Combine(_resources, "Only.icns"), 128);
		WritePlist("<key>CFBundleIconFile</key><string>Gone.icns</string>");

		var resolved = MacAppBundleIconLocator.TryResolveIconFile(_bundle);

		Assert.That(resolved, Is.EqualTo(Path.Combine(_resources, "Only.icns")));
	}

	[Test]
	public void TryResolveIconFile_NoIcnsInResources_ReturnsNull()
	{
		WriteBytes(Path.Combine(_resources, "readme.txt"), 32);
		WritePlist("<key>CFBundleName</key><string>Foo</string>");

		Assert.That(MacAppBundleIconLocator.TryResolveIconFile(_bundle), Is.Null);
	}

	[Test]
	public void TryResolveIconFile_BundleOrResourcesMissing_ReturnsNull()
	{
		Assert.Multiple(() =>
		{
			Assert.That(MacAppBundleIconLocator.TryResolveIconFile(Path.Combine(_root, "Absent.app")), Is.Null);
			Assert.That(MacAppBundleIconLocator.TryResolveIconFile(string.Empty), Is.Null);
		});
	}

	[Test]
	public void TryResolveIconFile_TrailingSeparator_ResolvesTheSameFile()
	{
		WriteBytes(Path.Combine(_resources, "AppIcon.icns"), 64);
		WritePlist("<key>CFBundleIconFile</key><string>AppIcon</string>");

		var resolved = MacAppBundleIconLocator.TryResolveIconFile(_bundle + Path.DirectorySeparatorChar);

		Assert.That(resolved, Is.EqualTo(Path.Combine(_resources, "AppIcon.icns")));
	}

	private void WritePlist(string body)
	{
		var xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
			"<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" " +
			"\"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">" +
			$"<plist version=\"1.0\"><dict>{body}</dict></plist>";
		File.WriteAllText(Path.Combine(_contents, "Info.plist"), xml, Encoding.UTF8);
	}

	private static void WriteBytes(string path, int length)
		=> File.WriteAllBytes(path, new byte[length]);
}
