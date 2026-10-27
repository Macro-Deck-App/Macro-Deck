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
	public void TearDown()
	{
		_paths.Cleanup();
	}

	[Test]
	public void DoesNothingWhenNoRestoreIsStaged()
	{
		Assert.That(StagedRestoreApplier.ApplyPending(_paths), Is.EqualTo(StagedRestoreOutcome.NothingPending));
	}

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
		Directory.Delete(_paths.PluginStagingDirectory, true);

		Stage([BackupComponentGroup.Plugins], ("plugins/demo/current.json", "after"));
		StagedRestoreApplier.ApplyPending(_paths);

		Assert.That(Directory.Exists(_paths.PluginStagingDirectory),
			Is.True,
			"plugin installs fail without their staging directory");
	}

	[Test]
	public void KeepsTheHostIdentityKeyWhenTheArchiveHasNone()
	{
		Write("keys/auth-signing.key", "current-signing");
		Write("keys/host-identity.key", "current-identity");
		Stage([BackupComponentGroup.Accounts], ("keys/auth-signing.key", "restored-signing"));

		StagedRestoreApplier.ApplyPending(_paths);

		Assert.Multiple(() =>
		{
			Assert.That(Read("keys/auth-signing.key"), Is.EqualTo("restored-signing"));
			Assert.That(Read("keys/host-identity.key"),
				Is.EqualTo("current-identity"),
				"an archive from before the identity key existed must not unpair every device");
		});
	}

	[Test]
	public void RestoresTheHostIdentityKeyWhenTheArchiveHasOne()
	{
		Write("keys/host-identity.key", "current-identity");
		Stage([BackupComponentGroup.Accounts], ("keys/host-identity.key", "restored-identity"));

		StagedRestoreApplier.ApplyPending(_paths);

		Assert.That(Read("keys/host-identity.key"), Is.EqualTo("restored-identity"));
	}

	[Test]
	public void An_apply_interrupted_while_moving_the_current_files_aside_leaves_the_original_data()
	{
		var document = StageKitchenOfficeRestore();
		Interrupt(document, ["data/profiles/kitchen.json"], []);

		var outcome = StagedRestoreApplier.ApplyPending(_paths);

		AssertOriginalProfiles(outcome);
	}

	[Test]
	public void An_apply_interrupted_while_moving_the_restored_files_in_leaves_the_original_data()
	{
		var document = StageKitchenOfficeRestore();
		Interrupt(document, ["data/profiles/kitchen.json", "data/profiles/office.json"], ["data/profiles/office.json"]);

		var outcome = StagedRestoreApplier.ApplyPending(_paths);

		AssertOriginalProfiles(outcome);
	}

	[Test]
	public void An_apply_interrupted_after_every_file_moved_leaves_the_original_data()
	{
		var document = StageKitchenOfficeRestore();
		Interrupt(document,
			["data/profiles/kitchen.json", "data/profiles/office.json"],
			["data/profiles/office.json", "data/profiles/studio.json"]);

		var outcome = StagedRestoreApplier.ApplyPending(_paths);

		AssertOriginalProfiles(outcome);
	}

	[Test]
	public void A_recovery_interrupted_part_way_is_completed_by_the_next_start()
	{
		var document = StageKitchenOfficeRestore();
		Interrupt(document,
			["data/profiles/kitchen.json", "data/profiles/office.json"],
			["data/profiles/office.json", "data/profiles/studio.json"]);
		MoveInto(document.ApplyDirectory, DataPath("data/profiles/office.json"), "data/profiles/office.json");
		MoveInto(document.ApplyDirectory, DataPath("data/profiles/studio.json"), "data/profiles/studio.json");
		MoveFromRollback(document, "data/profiles/kitchen.json");

		var outcome = StagedRestoreApplier.ApplyPending(_paths);

		AssertOriginalProfiles(outcome);
	}

	[Test]
	public void A_rollback_that_cannot_finish_keeps_everything_for_the_next_start()
	{
		var document = StageKitchenOfficeRestore();
		Interrupt(document, ["data/profiles/kitchen.json", "data/profiles/office.json"], ["data/profiles/office.json"]);
		var obstacle = Path.Combine(document.ApplyDirectory, "data", "profiles", "office.json");
		Directory.CreateDirectory(obstacle);

		var blocked = StagedRestoreApplier.ApplyPending(_paths);
		var keptForRetry = File.Exists(MarkerPath) && StagedRestoreApplier.HasBegun(document);
		Directory.Delete(obstacle);
		var retried = StagedRestoreApplier.ApplyPending(_paths);

		Assert.Multiple(() =>
		{
			Assert.That(blocked, Is.EqualTo(StagedRestoreOutcome.RollbackIncomplete));
			Assert.That(keptForRetry, Is.True, "the marker and the begun staging must survive for the next start");
		});
		AssertOriginalProfiles(retried);
	}

	[Test]
	public void A_restore_staged_after_a_recovered_interruption_applies_normally()
	{
		var interrupted = StageKitchenOfficeRestore();
		Interrupt(interrupted,
			["data/profiles/kitchen.json", "data/profiles/office.json"],
			["data/profiles/office.json"]);
		StagedRestoreApplier.ApplyPending(_paths);

		Stage([BackupComponentGroup.Profiles], ("data/profiles/office.json", "second-attempt"));
		var outcome = StagedRestoreApplier.ApplyPending(_paths);

		Assert.Multiple(() =>
		{
			Assert.That(outcome, Is.EqualTo(StagedRestoreOutcome.Applied));
			Assert.That(ProfileFiles(),
				Is.EquivalentTo(new Dictionary<string, string> { ["office.json"] = "second-attempt" }));
			Assert.That(File.Exists(MarkerPath), Is.False);
		});
	}

	[Test]
	public void A_failed_apply_also_removes_the_restored_files_that_had_no_local_counterpart()
	{
		Write("data/profiles/kitchen.json", "local-only");
		var document = Stage([BackupComponentGroup.Profiles],
			("data/profiles/new.json", "from-backup"),
			("data/profiles/studio.json", "from-backup"));
		Directory.CreateDirectory(Path.Combine(_paths.ProfilesDirectory, "studio.json"));

		var outcome = StagedRestoreApplier.ApplyPending(_paths);

		Assert.Multiple(() =>
		{
			Assert.That(outcome, Is.EqualTo(StagedRestoreOutcome.Abandoned));
			Assert.That(ProfileFiles(),
				Is.EquivalentTo(new Dictionary<string, string> { ["kitchen.json"] = "local-only" }));
			Assert.That(Directory.Exists(document.StagingDirectory), Is.False);
		});
	}

	private PendingRestoreDocument StageKitchenOfficeRestore()
	{
		Write("data/profiles/kitchen.json", "local-only");
		Write("data/profiles/office.json", "local-edit");
		Write("data/scripts/greet.json", "untouched");

		return Stage([BackupComponentGroup.Profiles],
			("data/profiles/office.json", "from-backup"),
			("data/profiles/studio.json", "from-backup"));
	}

	private void AssertOriginalProfiles(StagedRestoreOutcome outcome)
	{
		Assert.Multiple(() =>
		{
			Assert.That(outcome, Is.EqualTo(StagedRestoreOutcome.Abandoned));
			Assert.That(ProfileFiles(),
				Is.EquivalentTo(new Dictionary<string, string>
				{
					["kitchen.json"] = "local-only",
					["office.json"] = "local-edit"
				}),
				"an interrupted restore must leave the data exactly as it was before");
			Assert.That(Read("data/scripts/greet.json"), Is.EqualTo("untouched"));
			Assert.That(File.Exists(MarkerPath), Is.False, "the marker must go, or every start retries it forever");
		});
	}

	private void Interrupt(PendingRestoreDocument document, string[] movedAside, string[] applied)
	{
		File.WriteAllText(Path.Combine(document.StagingDirectory, "journal.json"), "[]");

		foreach (var relative in movedAside)
		{
			MoveInto(Path.Combine(document.StagingDirectory, "rollback"), DataPath(relative), relative);
		}

		foreach (var relative in applied)
		{
			var target = DataPath(relative);
			Directory.CreateDirectory(Path.GetDirectoryName(target)!);
			File.Move(Path.Combine(document.ApplyDirectory, Native(relative)), target);
		}
	}

	private void MoveFromRollback(PendingRestoreDocument document, string relative)
	{
		File.Move(Path.Combine(document.StagingDirectory, "rollback", Native(relative)), DataPath(relative));
	}

	private static void MoveInto(string root, string source, string relative)
	{
		var target = Path.Combine(root, Native(relative));
		Directory.CreateDirectory(Path.GetDirectoryName(target)!);
		File.Move(source, target);
	}

	private Dictionary<string, string> ProfileFiles()
	{
		return Directory.EnumerateFiles(_paths.ProfilesDirectory, "*", SearchOption.AllDirectories)
			.ToDictionary(path => Path.GetRelativePath(_paths.ProfilesDirectory, path).Replace('\\', '/'),
				File.ReadAllText);
	}

	private string DataPath(string relative)
	{
		return Path.Combine(_paths.BaseDirectory, Native(relative));
	}

	private static string Native(string relative)
	{
		return relative.Replace('/', Path.DirectorySeparatorChar);
	}

	private string MarkerPath => Path.Combine(_paths.RestoreStagingDirectory, PendingRestoreDocument.FileName);

	private PendingRestoreDocument Stage(
		BackupComponentGroup[] components,
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

		File.WriteAllText(MarkerPath, JsonSerializer.Serialize(document, _json));

		return document;
	}

	private static readonly JsonSerializerOptions _json =
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
	{
		return File.ReadAllText(Path.Combine(_paths.BaseDirectory,
			relativePath.Replace('/', Path.DirectorySeparatorChar)));
	}

	private bool Exists(string relativePath)
	{
		return File.Exists(Path.Combine(_paths.BaseDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)));
	}
}
