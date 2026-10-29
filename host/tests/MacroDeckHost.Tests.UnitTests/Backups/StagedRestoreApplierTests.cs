using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MacroDeckHost.Application.Backups;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Backups.Restore;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

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
	public void A_staged_file_that_points_outside_the_data_folder_is_never_moved_there()
	{
		Write("data/profiles/kitchen.json", "before");
		var outside = Path.Combine(Path.GetDirectoryName(_paths.BaseDirectory)!, $"md04-{Guid.NewGuid():N}.txt");
		var document = Stage([BackupComponentGroup.Profiles], ("data/profiles/kitchen.json", "after"));
		var escaping = $"data/profiles/../../../{Path.GetFileName(outside)}";
		var staged = Path.Combine(document.ApplyDirectory, Native(escaping));
		File.WriteAllText(staged, "payload");
		document.Files.Add(new PendingRestoreFile
		{
			RelativePath = escaping,
			Sha256 = Sha256(staged),
			Component = BackupComponentGroup.Profiles
		});
		File.WriteAllText(MarkerPath, JsonSerializer.Serialize(document, _json));

		try
		{
			var outcome = StagedRestoreApplier.ApplyPending(_paths);

			Assert.Multiple(() =>
			{
				Assert.That(outcome, Is.EqualTo(StagedRestoreOutcome.Abandoned));
				Assert.That(File.Exists(outside), Is.False);
				Assert.That(Read("data/profiles/kitchen.json"), Is.EqualTo("before"));
				Assert.That(File.Exists(MarkerPath), Is.False);
			});
		}
		finally
		{
			File.Delete(outside);
		}
	}

	[Test]
	public void A_staged_file_tagged_with_a_group_that_does_not_own_it_is_not_applied()
	{
		Write("data/profiles/kitchen.json", "before");
		Write("keys/auth-signing.key", "local-signing-key");
		var document = Stage([BackupComponentGroup.Profiles], ("data/profiles/kitchen.json", "after"));
		var staged = Path.Combine(document.ApplyDirectory, Native("keys/auth-signing.key"));
		Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
		File.WriteAllText(staged, "foreign-signing-key");
		document.Files.Add(new PendingRestoreFile
		{
			RelativePath = "keys/auth-signing.key",
			Sha256 = Sha256(staged),
			Component = BackupComponentGroup.Profiles
		});
		File.WriteAllText(MarkerPath, JsonSerializer.Serialize(document, _json));

		var outcome = StagedRestoreApplier.ApplyPending(_paths);

		Assert.Multiple(() =>
		{
			Assert.That(outcome, Is.EqualTo(StagedRestoreOutcome.Abandoned));
			Assert.That(Read("keys/auth-signing.key"), Is.EqualTo("local-signing-key"));
			Assert.That(Read("data/profiles/kitchen.json"), Is.EqualTo("before"));
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
	public void A_write_left_unpublished_before_the_restart_does_not_undo_the_restored_file()
	{
		Write("data/user-variables.json", VariablesJson("local"));
		Stage([BackupComponentGroup.Variables], ("data/user-variables.json", VariablesJson("from-backup")));
		Write("data/user-variables.json.tmp", VariablesJson("pending"));
		File.SetLastWriteTimeUtc(DataPath("data/user-variables.json.tmp"), DateTime.UtcNow.AddMinutes(1));

		StagedRestoreApplier.ApplyPending(_paths);
		var loaded = new JsonUserVariableStore(_paths, new LoggerConfiguration().CreateLogger()).Load();

		Assert.That(loaded.Single().Value, Is.EqualTo("from-backup"));
	}

	[Test]
	public void A_quarantined_copy_survives_a_restore_of_its_group()
	{
		Write("data/user-variables.json", VariablesJson("local"));
		Write("data/user-variables.json.corrupt-20261028-120000-000", "damaged");
		Stage([BackupComponentGroup.Variables], ("data/user-variables.json", VariablesJson("from-backup")));

		StagedRestoreApplier.ApplyPending(_paths);

		Assert.That(Read("data/user-variables.json.corrupt-20261028-120000-000"), Is.EqualTo("damaged"));
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

	[Test]
	public void A_failed_apply_removes_the_folders_it_created_and_keeps_the_ones_that_were_there()
	{
		Write("data/profiles/kitchen.json", "local-only");
		Directory.CreateDirectory(DataPath("data/profiles/empty-local"));
		Stage([BackupComponentGroup.Profiles, BackupComponentGroup.Plugins],
			("plugins/new-plugin/versions/1.0.0/manifest.json", "from-backup"),
			("data/profiles/nested/deeper/new.json", "from-backup"),
			("data/profiles/studio.json", "from-backup"));
		Directory.CreateDirectory(Path.Combine(_paths.ProfilesDirectory, "studio.json"));

		var outcome = StagedRestoreApplier.ApplyPending(_paths);

		Assert.Multiple(() =>
		{
			Assert.That(outcome, Is.EqualTo(StagedRestoreOutcome.Abandoned));
			Assert.That(ProfileFiles(),
				Is.EquivalentTo(new Dictionary<string, string> { ["kitchen.json"] = "local-only" }));
			Assert.That(Directory.Exists(DataPath("plugins/new-plugin")),
				Is.False,
				"a folder the restore created would show up as an installed plugin without versions");
			Assert.That(Directory.Exists(DataPath("data/profiles/nested")), Is.False);
			Assert.That(Directory.Exists(DataPath("data/profiles/empty-local")),
				Is.True,
				"a folder that existed before the restore is the user's, even when empty");
			Assert.That(Directory.Exists(_paths.PluginsDirectory), Is.True);
		});
	}

	[Test]
	public void A_failed_apply_brings_back_a_file_where_the_restore_created_a_folder_of_the_same_name()
	{
		Write("data/profiles/kitchen.json", "local-only");
		Write("data/profiles/office", "local-file");
		Stage([BackupComponentGroup.Profiles],
			("data/profiles/office/desk.json", "from-backup"),
			("data/profiles/studio.json", "from-backup"));
		Directory.CreateDirectory(Path.Combine(_paths.ProfilesDirectory, "studio.json"));

		var outcome = StagedRestoreApplier.ApplyPending(_paths);

		Assert.Multiple(() =>
		{
			Assert.That(outcome, Is.EqualTo(StagedRestoreOutcome.Abandoned));
			Assert.That(ProfileFiles(),
				Is.EquivalentTo(new Dictionary<string, string>
				{
					["kitchen.json"] = "local-only",
					["office"] = "local-file"
				}));
		});
	}

	[Test]
	public void A_failed_table_copy_rolls_back_every_restored_file()
	{
		Write("data/profiles/kitchen.json", "local-only");
		Write("data/profiles/office.json", "local-edit");
		var document = Stage([BackupComponentGroup.Profiles, BackupComponentGroup.AppSettings],
			("data/profiles/office.json", "from-backup"),
			("data/profiles/studio.json", "from-backup"),
			("resources/new-resource.txt", "from-backup"));
		var unreadableDatabase = Path.Combine(document.StagingDirectory, "database.db");
		File.WriteAllText(unreadableDatabase, "not a database");
		document.DatabasePath = unreadableDatabase;
		document.Tables = ["app_preference"];
		File.WriteAllText(MarkerPath, JsonSerializer.Serialize(document, _json));

		var outcome = StagedRestoreApplier.ApplyPending(_paths);

		Assert.Multiple(() =>
		{
			Assert.That(outcome, Is.EqualTo(StagedRestoreOutcome.Abandoned));
			Assert.That(ProfileFiles(),
				Is.EquivalentTo(new Dictionary<string, string>
				{
					["kitchen.json"] = "local-only",
					["office.json"] = "local-edit"
				}));
			Assert.That(Exists("resources/new-resource.txt"), Is.False);
			Assert.That(File.Exists(MarkerPath), Is.False);
		});
	}

	[Test]
	[Platform(Exclude = "Win")]
	[UnsupportedOSPlatform("windows")]
	public void A_rollback_denied_access_to_a_file_does_not_stop_the_start_and_finishes_later()
	{
		var document = StageKitchenOfficeRestore();
		Interrupt(document,
			["data/profiles/kitchen.json", "data/profiles/office.json"],
			["data/profiles/office.json", "data/profiles/studio.json"]);
		var profilesMode = File.GetUnixFileMode(_paths.ProfilesDirectory);
		File.SetUnixFileMode(_paths.ProfilesDirectory, UnixFileMode.UserRead | UnixFileMode.UserExecute);

		StagedRestoreOutcome blocked;
		try
		{
			blocked = StagedRestoreApplier.ApplyPending(_paths);
		}
		finally
		{
			File.SetUnixFileMode(_paths.ProfilesDirectory, profilesMode);
		}

		var retried = StagedRestoreApplier.ApplyPending(_paths);

		Assert.That(blocked, Is.EqualTo(StagedRestoreOutcome.RollbackIncomplete));
		AssertOriginalProfiles(retried);
	}

	[Test]
	[Platform(Exclude = "Win")]
	[UnsupportedOSPlatform("windows")]
	public void A_staging_folder_denied_access_after_a_rollback_does_not_stop_the_start()
	{
		Write("data/profiles/kitchen.json", "local-only");
		Stage([BackupComponentGroup.Profiles],
			("data/profiles/new.json", "from-backup"),
			("data/profiles/studio.json", "from-backup"));
		Directory.CreateDirectory(Path.Combine(_paths.ProfilesDirectory, "studio.json"));
		var stagingMode = File.GetUnixFileMode(_paths.RestoreStagingDirectory);
		File.SetUnixFileMode(_paths.RestoreStagingDirectory, UnixFileMode.UserRead | UnixFileMode.UserExecute);

		StagedRestoreOutcome denied;
		try
		{
			denied = StagedRestoreApplier.ApplyPending(_paths);
		}
		finally
		{
			File.SetUnixFileMode(_paths.RestoreStagingDirectory, stagingMode);
		}

		var retried = StagedRestoreApplier.ApplyPending(_paths);

		Assert.Multiple(() =>
		{
			Assert.That(denied, Is.EqualTo(StagedRestoreOutcome.Abandoned));
			Assert.That(retried, Is.EqualTo(StagedRestoreOutcome.Abandoned));
			Assert.That(ProfileFiles(),
				Is.EquivalentTo(new Dictionary<string, string> { ["kitchen.json"] = "local-only" }));
			Assert.That(File.Exists(MarkerPath), Is.False);
		});
	}

	[Test]
	[Platform(Exclude = "Win")]
	[UnsupportedOSPlatform("windows")]
	public void A_staging_folder_that_cannot_be_deleted_after_an_apply_does_not_stop_the_start()
	{
		Write("data/profiles/office.json", "local-edit");
		var document = Stage([BackupComponentGroup.Profiles], ("data/profiles/office.json", "from-backup"));
		var locked = Path.Combine(document.StagingDirectory, "locked");
		Directory.CreateDirectory(locked);
		File.WriteAllText(Path.Combine(locked, "leftover.txt"), "x");
		File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserExecute);

		StagedRestoreOutcome outcome;
		try
		{
			outcome = StagedRestoreApplier.ApplyPending(_paths);
		}
		finally
		{
			File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		}

		Assert.Multiple(() =>
		{
			Assert.That(outcome, Is.EqualTo(StagedRestoreOutcome.Applied));
			Assert.That(Read("data/profiles/office.json"), Is.EqualTo("from-backup"));
			Assert.That(File.Exists(MarkerPath), Is.False);
		});
	}

	[Test]
	[Platform(Exclude = "Win")]
	[UnsupportedOSPlatform("windows")]
	public void A_retried_rollback_also_removes_the_folders_the_interrupted_apply_created()
	{
		var document = StageKitchenOfficeRestore();
		document.Components.Add(BackupComponentGroup.Plugins);
		AddStagedFile(document, "plugins/new-plugin/versions/1.0.0/manifest.json", "from-backup");
		Interrupt(document,
			["data/profiles/kitchen.json", "data/profiles/office.json"],
			[
				"data/profiles/office.json",
				"data/profiles/studio.json",
				"plugins/new-plugin/versions/1.0.0/manifest.json"
			],
			["plugins/new-plugin", "plugins/new-plugin/versions", "plugins/new-plugin/versions/1.0.0"]);
		var profilesMode = File.GetUnixFileMode(_paths.ProfilesDirectory);
		File.SetUnixFileMode(_paths.ProfilesDirectory, UnixFileMode.UserRead | UnixFileMode.UserExecute);

		StagedRestoreOutcome blocked;
		try
		{
			blocked = StagedRestoreApplier.ApplyPending(_paths);
		}
		finally
		{
			File.SetUnixFileMode(_paths.ProfilesDirectory, profilesMode);
		}

		var retried = StagedRestoreApplier.ApplyPending(_paths);

		Assert.That(blocked, Is.EqualTo(StagedRestoreOutcome.RollbackIncomplete));
		AssertOriginalProfiles(retried);
		Assert.That(Directory.Exists(DataPath("plugins/new-plugin")), Is.False);
	}

	[Test]
	[Platform(Exclude = "Win")]
	[UnsupportedOSPlatform("windows")]
	public void A_created_folder_that_cannot_be_removed_still_brings_the_originals_back()
	{
		var document = StageKitchenOfficeRestore();
		document.Components.Add(BackupComponentGroup.Plugins);
		AddStagedFile(document, "plugins/new-plugin/versions/1.0.0/manifest.json", "from-backup");
		Interrupt(document,
			["data/profiles/kitchen.json", "data/profiles/office.json"],
			[
				"data/profiles/office.json",
				"data/profiles/studio.json",
				"plugins/new-plugin/versions/1.0.0/manifest.json"
			],
			["plugins/new-plugin", "plugins/new-plugin/versions", "plugins/new-plugin/versions/1.0.0"]);
		var pluginsMode = File.GetUnixFileMode(_paths.PluginsDirectory);
		File.SetUnixFileMode(_paths.PluginsDirectory, UnixFileMode.UserRead | UnixFileMode.UserExecute);

		StagedRestoreOutcome blocked;
		Dictionary<string, string> profilesWhileBlocked;
		try
		{
			blocked = StagedRestoreApplier.ApplyPending(_paths);
			profilesWhileBlocked = ProfileFiles();
		}
		finally
		{
			File.SetUnixFileMode(_paths.PluginsDirectory, pluginsMode);
		}

		var retried = StagedRestoreApplier.ApplyPending(_paths);

		Assert.Multiple(() =>
		{
			Assert.That(blocked, Is.EqualTo(StagedRestoreOutcome.RollbackIncomplete));
			Assert.That(profilesWhileBlocked,
				Is.EquivalentTo(new Dictionary<string, string>
				{
					["kitchen.json"] = "local-only",
					["office.json"] = "local-edit"
				}),
				"a leftover folder must not keep the user's own files away");
		});
		AssertOriginalProfiles(retried);
		Assert.That(Directory.Exists(DataPath("plugins/new-plugin")), Is.False);
	}

	[Test]
	[Platform(Exclude = "Win")]
	[UnsupportedOSPlatform("windows")]
	public void A_marker_that_cannot_be_deleted_after_an_apply_never_rolls_the_restore_back()
	{
		Write("data/profiles/office.json", "local-edit");
		Stage([BackupComponentGroup.Profiles], ("data/profiles/office.json", "from-backup"));
		var stagingMode = File.GetUnixFileMode(_paths.RestoreStagingDirectory);
		File.SetUnixFileMode(_paths.RestoreStagingDirectory, UnixFileMode.UserRead | UnixFileMode.UserExecute);

		StagedRestoreOutcome applied;
		try
		{
			applied = StagedRestoreApplier.ApplyPending(_paths);
		}
		finally
		{
			File.SetUnixFileMode(_paths.RestoreStagingDirectory, stagingMode);
		}

		StagedRestoreApplier.ApplyPending(_paths);

		Assert.Multiple(() =>
		{
			Assert.That(applied, Is.EqualTo(StagedRestoreOutcome.Applied));
			Assert.That(Read("data/profiles/office.json"),
				Is.EqualTo("from-backup"),
				"the tables are already committed, so the files must not go back");
			Assert.That(File.Exists(MarkerPath), Is.False);
		});
	}

	[Test]
	[Platform(Exclude = "Win")]
	[UnsupportedOSPlatform("windows")]
	public void A_staged_file_that_cannot_be_read_leaves_the_installation_untouched()
	{
		Write("data/profiles/office.json", "local-edit");
		var document = Stage([BackupComponentGroup.Profiles], ("data/profiles/office.json", "from-backup"));
		File.SetUnixFileMode(Path.Combine(document.ApplyDirectory, "data", "profiles", "office.json"),
			UnixFileMode.None);

		var outcome = StagedRestoreApplier.ApplyPending(_paths);

		Assert.Multiple(() =>
		{
			Assert.That(outcome, Is.EqualTo(StagedRestoreOutcome.Abandoned));
			Assert.That(Read("data/profiles/office.json"), Is.EqualTo("local-edit"));
			Assert.That(File.Exists(MarkerPath), Is.False);
		});
	}

	[Test]
	[Platform(Exclude = "Win")]
	[UnsupportedOSPlatform("windows")]
	public void A_marker_that_cannot_be_read_does_not_stop_the_start()
	{
		Write("data/profiles/office.json", "local-edit");
		Stage([BackupComponentGroup.Profiles], ("data/profiles/office.json", "from-backup"));
		File.SetUnixFileMode(MarkerPath, UnixFileMode.None);

		var outcome = StagedRestoreApplier.ApplyPending(_paths);

		Assert.Multiple(() =>
		{
			Assert.That(outcome, Is.EqualTo(StagedRestoreOutcome.Abandoned));
			Assert.That(Read("data/profiles/office.json"), Is.EqualTo("local-edit"));
			Assert.That(File.Exists(MarkerPath), Is.False);
		});
	}

	[Test]
	[Platform(Exclude = "Win")]
	[UnsupportedOSPlatform("windows")]
	public void A_data_folder_that_cannot_be_written_postpones_the_restore_without_stopping_the_start()
	{
		Write("data/profiles/office.json", "local-edit");
		Stage([BackupComponentGroup.Profiles], ("data/profiles/office.json", "from-backup"));
		var rootMode = File.GetUnixFileMode(_paths.DataRootDirectory);
		File.SetUnixFileMode(_paths.DataRootDirectory, UnixFileMode.UserRead | UnixFileMode.UserExecute);

		StagedRestoreOutcome outcome;
		try
		{
			outcome = StagedRestoreApplier.ApplyPending(_paths);
		}
		finally
		{
			File.SetUnixFileMode(_paths.DataRootDirectory, rootMode);
		}

		Assert.Multiple(() =>
		{
			Assert.That(outcome, Is.EqualTo(StagedRestoreOutcome.Locked));
			Assert.That(Read("data/profiles/office.json"), Is.EqualTo("local-edit"));
			Assert.That(File.Exists(MarkerPath), Is.True, "the restore is still pending for the next start");
		});
	}

	private void AddStagedFile(PendingRestoreDocument document, string relative, string content)
	{
		var target = Path.Combine(document.ApplyDirectory, Native(relative));
		Directory.CreateDirectory(Path.GetDirectoryName(target)!);
		File.WriteAllText(target, content);

		document.Files.Add(new PendingRestoreFile
		{
			RelativePath = relative,
			Sha256 = Sha256(target),
			Component = BackupComponentGroups.Owner(relative)!.Value
		});
		File.WriteAllText(MarkerPath, JsonSerializer.Serialize(document, _json));
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

	private void Interrupt(
		PendingRestoreDocument document,
		string[] movedAside,
		string[] applied,
		string[]? createdDirectories = null)
	{
		var journal = createdDirectories is null
			? "[]"
			: JsonSerializer.Serialize(new { CreatedDirectories = createdDirectories }, _json);
		File.WriteAllText(Path.Combine(document.StagingDirectory, "journal.json"), journal);

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

	private static string VariablesJson(string value)
	{
		return "[{\"id\":\"01a18443-5f55-76cf-a8fa-26c30240d6b8\",\"name\":\"restored\",\"scope\":\"Global\","
			+ $"\"type\":\"Text\",\"classification\":\"User\",\"value\":\"{value}\"}}]";
	}

	private bool Exists(string relativePath)
	{
		return File.Exists(Path.Combine(_paths.BaseDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)));
	}
}
