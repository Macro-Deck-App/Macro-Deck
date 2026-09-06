using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MacroDeckHost.Application.Backups;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Backups.Restore;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Backups;

[TestFixture]
public class StagedRestoreApplierTests
{
	private TestPaths _paths = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		_paths.EnsureDirectoriesExist();
	}

	[TearDown]
	public void TearDown() => _paths.Cleanup();

	[Test]
	public void DoesNothingWhenNoRestoreIsStaged()
		=> Assert.That(StagedRestoreApplier.ApplyPending(_paths), Is.EqualTo(StagedRestoreOutcome.NothingPending));

	[Test]
	public void ReplacesTheSelectedGroupAndRemovesWhatTheBackupDoesNotHave()
	{
		Write("data/profiles/kitchen.json", "local-only");
		Write("data/profiles/office.json", "local-edit");
		Write("data/scripts/greet.json", "untouched");

		Stage([BackupComponentGroup.Profiles],
			("data/profiles/office.json", "from-backup"),
			("data/profiles/studio.json", "from-backup"));

		var outcome = StagedRestoreApplier.ApplyPending(_paths);

		Assert.Multiple(() =>
		{
			Assert.That(outcome, Is.EqualTo(StagedRestoreOutcome.Applied));
			Assert.That(Read("data/profiles/office.json"), Is.EqualTo("from-backup"));
			Assert.That(Read("data/profiles/studio.json"), Is.EqualTo("from-backup"));
			Assert.That(Exists("data/profiles/kitchen.json"),
				Is.False,
				"a restore replaces the group wholesale, it does not merge into it");
			Assert.That(Read("data/scripts/greet.json"),
				Is.EqualTo("untouched"),
				"an unselected group must not be touched");
		});
	}

	[Test]
	public void LeavesTheInstallationUntouchedWhenTheStagedContentIsDamaged()
	{
		Write("data/profiles/kitchen.json", "before");

		var document = Stage([BackupComponentGroup.Profiles], ("data/profiles/kitchen.json", "after"));
		File.WriteAllText(Path.Combine(document.ApplyDirectory, "data", "profiles", "kitchen.json"), "tampered");

		var outcome = StagedRestoreApplier.ApplyPending(_paths);

		Assert.Multiple(() =>
		{
			Assert.That(outcome, Is.EqualTo(StagedRestoreOutcome.Abandoned));
			Assert.That(Read("data/profiles/kitchen.json"), Is.EqualTo("before"));
			Assert.That(File.Exists(MarkerPath), Is.False, "the marker must go, or every start retries it forever");
		});
	}

	[Test]
	public void AppliesOnlyOnceAcrossRepeatedStarts()
	{
		Write("data/profiles/kitchen.json", "before");
		Stage([BackupComponentGroup.Profiles], ("data/profiles/kitchen.json", "after"));

		var first = StagedRestoreApplier.ApplyPending(_paths);
		Write("data/profiles/kitchen.json", "changed-after-restore");
		var second = StagedRestoreApplier.ApplyPending(_paths);

		Assert.Multiple(() =>
		{
			Assert.That(first, Is.EqualTo(StagedRestoreOutcome.Applied));
			Assert.That(second, Is.EqualTo(StagedRestoreOutcome.NothingPending));
			Assert.That(Read("data/profiles/kitchen.json"), Is.EqualTo("changed-after-restore"));
		});
	}

	[Test]
	public void RecreatesTheWorkingDirectoriesTheArchiveDeliberatelyOmits()
	{
		Write("plugins/demo/current.json", "before");
		Directory.Delete(_paths.PluginStagingDirectory, recursive: true);

		Stage([BackupComponentGroup.Plugins], ("plugins/demo/current.json", "after"));
		StagedRestoreApplier.ApplyPending(_paths);

		Assert.That(Directory.Exists(_paths.PluginStagingDirectory),
			Is.True,
			"plugin installs fail without their staging directory");
	}

	private string MarkerPath => Path.Combine(_paths.RestoreStagingDirectory, PendingRestoreDocument.FileName);

	private PendingRestoreDocument Stage(BackupComponentGroup[] components,
		params (string Path, string Content)[] files)
	{
		var staging = Path.Combine(_paths.RestoreStagingDirectory, Guid.NewGuid().ToString("N"));
		var apply = Path.Combine(staging, "apply");
		Directory.CreateDirectory(apply);

		var document = new PendingRestoreDocument
		{
			RestoreId = Guid.NewGuid(),
			BackupId = Guid.NewGuid(),
			StagedAt = DateTimeOffset.UnixEpoch,
			Components = [.. components],
			StagingDirectory = staging,
			ApplyDirectory = apply
		};

		foreach (var (relative, content) in files)
		{
			var target = Path.Combine(apply, relative.Replace('/', Path.DirectorySeparatorChar));
			Directory.CreateDirectory(Path.GetDirectoryName(target)!);
			File.WriteAllText(target, content);

			document.Files.Add(new PendingRestoreFile
			{
				RelativePath = relative,
				Sha256 = Sha256(target),
				Component = BackupComponentGroups.Owner(relative)!.Value
			});
		}

		File.WriteAllText(MarkerPath, JsonSerializer.Serialize(document, Json));

		return document;
	}

	private static readonly JsonSerializerOptions Json =
		new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

	private static string Sha256(string path)
	{
		using var stream = File.OpenRead(path);

		return Convert.ToHexStringLower(SHA256.HashData(stream));
	}

	private void Write(string relativePath, string content)
	{
		var path = Path.Combine(_paths.BaseDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, content, Encoding.UTF8);
	}

	private string Read(string relativePath)
		=> File.ReadAllText(Path.Combine(_paths.BaseDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)));

	private bool Exists(string relativePath)
		=> File.Exists(Path.Combine(_paths.BaseDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)));
}
