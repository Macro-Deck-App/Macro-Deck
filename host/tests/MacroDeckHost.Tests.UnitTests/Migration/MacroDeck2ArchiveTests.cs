using System.IO.Compression;
using System.Text;
using MacroDeckHost.Infrastructure.Migration.MacroDeck2;

namespace MacroDeckHost.Tests.UnitTests.Migration;

[TestFixture]
public class MacroDeck2ArchiveTests
{
	private string _zipPath = null!;

	[SetUp]
	public void SetUp()
		=> _zipPath = Path.Combine(Path.GetTempPath(), $"md2-backup-{Guid.NewGuid():N}.zip");

	[TearDown]
	public void TearDown()
	{
		if (File.Exists(_zipPath))
		{
			File.Delete(_zipPath);
		}
	}

	[Test]
	public void TryOpen_UnpacksTheDataDirectoryABackupHoldsAtItsRoot()
	{
		WriteZip(new Dictionary<string, string>
		{
			["config.json"] = """{"Language":"English"}""",
			["profiles/0.json"] = """{"ProfileId":"0"}""",
			["iconpacks/pack/ExtensionManifest.json"] = """{"name":"Pack"}""",
			["credentials/vendor_plugin"] = "[]"
		});

		using var archive = MacroDeck2Archive.TryOpen(_zipPath);

		Assert.That(archive, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(File.Exists(Path.Combine(archive!.Root, "config.json")), Is.True);
			Assert.That(File.Exists(Path.Combine(archive.Root, "profiles", "0.json")), Is.True);
			Assert.That(File.Exists(Path.Combine(archive.Root, "iconpacks", "pack", "ExtensionManifest.json")),
				Is.True);
			Assert.That(File.Exists(Path.Combine(archive.Root, "credentials", "vendor_plugin")), Is.True);
		});
	}

	// Macro Deck 2 writes its backups on Windows, and the ones in the wild mix both separators inside one
	// archive - an entry named with backslashes must land in the same place as one named with slashes.
	[Test]
	public void TryOpen_ReadsEntryNamesThatUseWindowsSeparators()
	{
		WriteZip(new Dictionary<string, string>
		{
			["config.json"] = "{}",
			[@"iconpacks\pack\icon.png"] = "not really a png"
		});

		using var archive = MacroDeck2Archive.TryOpen(_zipPath);

		Assert.That(File.Exists(Path.Combine(archive!.Root, "iconpacks", "pack", "icon.png")), Is.True);
	}

	// A backup is mostly plugin binaries - 95 of 102 MB in a real one, including an 80 MB executable -
	// and none of it is readable by a migration.
	[Test]
	public void TryOpen_LeavesThePluginBinariesInTheArchive()
	{
		WriteZip(new Dictionary<string, string>
		{
			["config.json"] = "{}",
			[@"plugins\Vendor.Plugin\Vendor.dll"] = new('x', 4096)
		});

		using var archive = MacroDeck2Archive.TryOpen(_zipPath);

		Assert.That(Directory.Exists(Path.Combine(archive!.Root, "plugins")), Is.False);
	}

	[TestCase(@"..\..\escaped.json")]
	[TestCase("profiles/../../escaped.json")]
	[TestCase("/etc/passwd")]
	[TestCase(@"C:\Windows\system.ini")]
	public void TryOpen_RefusesAnEntryThatWouldEscapeTheDirectory(string entryName)
	{
		WriteZip(new Dictionary<string, string> { ["config.json"] = "{}", [entryName] = "owned" });

		using var archive = MacroDeck2Archive.TryOpen(_zipPath);

		Assert.That(Directory.EnumerateFiles(archive!.Root, "*", SearchOption.AllDirectories)
				.Select(Path.GetFileName),
			Is.EquivalentTo(new[] { "config.json" }));
	}

	// A data folder compressed by hand nests everything under one directory - the shape a right-click
	// produces - while Macro Deck 2's own backups write their entries at the root. Both must read alike.
	[Test]
	public void TryOpen_UnpacksAFolderThatWasSimplyCompressed()
	{
		WriteZip(new Dictionary<string, string>
		{
			["Macro Deck/config.json"] = """{"Language":"English"}""",
			["Macro Deck/profiles/0.json"] = """{"ProfileId":"0"}""",
			["Macro Deck/iconpacks/pack/ExtensionManifest.json"] = """{"name":"Pack"}"""
		});

		using var archive = MacroDeck2Archive.TryOpen(_zipPath);

		Assert.That(archive, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(File.Exists(Path.Combine(archive!.Root, "config.json")), Is.True);
			Assert.That(File.Exists(Path.Combine(archive.Root, "profiles", "0.json")), Is.True);
			Assert.That(Directory.Exists(Path.Combine(archive.Root, "Macro Deck")),
				Is.False,
				"the wrapping directory is stripped, not carried into the unpacked copy");
		});
	}

	[Test]
	public void TryOpen_UnpacksAFolderNestedMoreThanOneLevelDeep()
	{
		WriteZip(new Dictionary<string, string>
		{
			["Downloads/Macro Deck/config.json"] = "{}",
			["Downloads/Macro Deck/profiles/0.json"] = """{"ProfileId":"0"}"""
		});

		using var archive = MacroDeck2Archive.TryOpen(_zipPath);

		Assert.That(File.Exists(Path.Combine(archive!.Root, "profiles", "0.json")), Is.True);
	}

	// Only a directory shared by *every* entry is a wrapper. An archive whose entries genuinely start at
	// "iconpacks/" must not have that stripped, which would leave nothing recognisable behind.
	[Test]
	public void TryOpen_DoesNotStripADirectoryThatIsPartOfTheDataItself()
	{
		WriteZip(new Dictionary<string, string>
		{
			["iconpacks/pack/ExtensionManifest.json"] = """{"name":"Pack"}""",
			["iconpacks/pack/icon.png"] = "bytes"
		});

		// Nothing here identifies a Macro Deck 2 directory, so it is refused rather than half-read.
		Assert.That(MacroDeck2Archive.TryOpen(_zipPath), Is.Null);
	}

	[Test]
	public void TryOpen_WithAnArchiveThatHoldsNoMacroDeck2Data_ReportsThatItIsUnusable()
	{
		WriteZip(new Dictionary<string, string> { ["notes.txt"] = "nothing to see" });

		Assert.That(MacroDeck2Archive.TryOpen(_zipPath), Is.Null);
	}

	[Test]
	public void TryOpen_WithAFileThatIsNotAZip_ReportsThatItIsUnusable()
	{
		File.WriteAllText(_zipPath, "this is not a zip");

		Assert.That(MacroDeck2Archive.TryOpen(_zipPath), Is.Null);
	}

	[Test]
	public void Dispose_RemovesTheUnpackedCopy()
	{
		WriteZip(new Dictionary<string, string> { ["config.json"] = "{}" });

		string root;
		using (var archive = MacroDeck2Archive.TryOpen(_zipPath))
		{
			root = archive!.Root;
			Assert.That(Directory.Exists(root), Is.True);
		}

		Assert.That(Directory.Exists(root), Is.False);
	}

	private void WriteZip(IReadOnlyDictionary<string, string> entries)
	{
		using var stream = File.Create(_zipPath);
		using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
		foreach (var (name, content) in entries)
		{
			var entry = zip.CreateEntry(name);
			using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
			writer.Write(content);
		}
	}
}
