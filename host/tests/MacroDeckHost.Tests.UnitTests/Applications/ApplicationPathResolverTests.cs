using System.Runtime.Versioning;
using MacroDeckHost.Infrastructure.Applications;

namespace MacroDeckHost.Tests.UnitTests.Applications;

[TestFixture]
public class ApplicationPathResolverTests
{
	private string _root = null!;
	private ApplicationPathResolver _resolver = null!;

	[SetUp]
	public void SetUp()
	{
		_root = Directory.CreateTempSubdirectory("md-app-resolver").FullName;
		_resolver = new ApplicationPathResolver();
	}

	[TearDown]
	public void TearDown()
	{
		try
		{
			Directory.Delete(_root, recursive: true);
		}
		catch (IOException)
		{
		}
	}

	private string WriteFile(string name, string content = "")
	{
		var path = Path.Combine(_root, name);
		File.WriteAllText(path, content);
		return path;
	}

	private string MakeDirectory(string name)
	{
		var path = Path.Combine(_root, name);
		Directory.CreateDirectory(path);
		return path;
	}

	[Test]
	public void Resolve_AppBundle_KeepsTheBundlePath()
	{
		var bundle = MakeDirectory("Calculator.app");

		var resolved = _resolver.Resolve(bundle);

		Assert.Multiple(() =>
		{
			Assert.That(resolved!.Path, Is.EqualTo(bundle));
			Assert.That(resolved.Name, Is.EqualTo("Calculator"));
			Assert.That(resolved.Arguments, Is.Null);
		});
	}

	// A dropped directory arrives with a trailing separator on some platforms, which would otherwise
	// hide the bundle's extension.
	[Test]
	public void Resolve_AppBundleWithTrailingSeparator_IsStillABundle()
	{
		var bundle = MakeDirectory("Calculator.app");

		Assert.That(_resolver.Resolve(bundle + Path.DirectorySeparatorChar)!.Name, Is.EqualTo("Calculator"));
	}

	[Test]
	public void Resolve_DesktopEntry_ResolvesToItsExecTarget()
	{
		var entry = WriteFile("firefox.desktop",
			"""
			[Desktop Entry]
			Name=Firefox Web Browser
			Exec=/usr/bin/firefox --new-window %u
			Icon=firefox
			""");

		var resolved = _resolver.Resolve(entry);

		Assert.Multiple(() =>
		{
			Assert.That(resolved!.Path, Is.EqualTo("/usr/bin/firefox"));
			Assert.That(resolved.Arguments, Is.EqualTo("--new-window"));
			Assert.That(resolved.Name, Is.EqualTo("Firefox Web Browser"));
		});
	}

	[Test]
	public void Resolve_DesktopEntryWithoutExec_IsRefused()
	{
		var entry = WriteFile("broken.desktop", "[Desktop Entry]\nName=Broken\n");

		Assert.That(_resolver.Resolve(entry), Is.Null);
	}

	[Test]
	public void Resolve_ShortcutsAndExecutables_KeepTheirPath()
	{
		Assert.Multiple(() =>
		{
			Assert.That(_resolver.Resolve(WriteFile("app.exe"))!.Name, Is.EqualTo("app"));
			Assert.That(_resolver.Resolve(WriteFile("App.lnk"))!.Name, Is.EqualTo("App"));
			Assert.That(_resolver.Resolve(WriteFile("Site.url"))!.Name, Is.EqualTo("Site"));
			Assert.That(_resolver.Resolve(WriteFile("run.sh"))!.Name, Is.EqualTo("run"));
		});
	}

	[Test]
	public void Resolve_PlainFolderOrDocument_IsRefused()
	{
		Assert.Multiple(() =>
		{
			Assert.That(_resolver.Resolve(MakeDirectory("Documents")), Is.Null);
			Assert.That(_resolver.Resolve(WriteFile("notes.md", "hello")), Is.Null);
			Assert.That(_resolver.Resolve(Path.Combine(_root, "gone.exe")), Is.Null);
			Assert.That(_resolver.Resolve("   "), Is.Null);
		});
	}

	[Test]
	[Platform(Exclude = "Win", Reason = "Creating a symlink needs elevation on Windows")]
	public void Resolve_SymlinkToABundle_ResolvesToTheBundle()
	{
		var bundle = MakeDirectory("Calculator.app");
		var link = Path.Combine(_root, "Calculator alias");
		Directory.CreateSymbolicLink(link, bundle);

		var resolved = _resolver.Resolve(link);

		Assert.That(resolved!.Name, Is.EqualTo("Calculator"));
	}

	[Test]
	[Platform(Exclude = "Win", Reason = "Unix file modes")]
	[UnsupportedOSPlatform("windows")]
	public void Resolve_ExtensionlessFile_FollowsTheExecutableBit()
	{
		var executable = WriteFile("tool");
		File.SetUnixFileMode(executable,
			UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		var plain = WriteFile("readme");
		File.SetUnixFileMode(plain, UnixFileMode.UserRead | UnixFileMode.UserWrite);

		Assert.Multiple(() =>
		{
			Assert.That(_resolver.Resolve(executable)!.Name, Is.EqualTo("tool"));
			Assert.That(_resolver.Resolve(plain), Is.Null);
		});
	}
}
