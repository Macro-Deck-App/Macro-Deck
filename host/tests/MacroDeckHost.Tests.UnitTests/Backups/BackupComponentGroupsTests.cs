using MacroDeckHost.Application.Backups;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.Backups;

[TestFixture]
public class BackupComponentGroupsTests
{
	[Test]
	public void Profiles_Requires_Variables()
	{
		var profiles = BackupComponentGroups.Definition(BackupComponentGroup.Profiles);

		// A selective restore of Profiles alone recreates widgets. Without this dependency the restored
		// widgets would come back with their widget-scoped variables silently dropped - the same bug the
		// export/import path was fixed for, just on the backup path instead.
		Assert.That(profiles.Requires, Does.Contain(BackupComponentGroup.Variables));
	}

	[Test]
	public void Onboarding_state_is_never_restored_while_ordinary_settings_still_are()
	{
		Assert.Multiple(() =>
		{
			// It records what this installation has already shown its user, so a restore must neither
			// resurrect a finished wizard nor erase one that is still owed.
			Assert.That(BackupComponentGroups.IsRestorablePreferenceKey("onboarding.pending"), Is.False);
			Assert.That(BackupComponentGroups.IsRestorablePreferenceKey("identity.issued"), Is.False);
			Assert.That(BackupComponentGroups.IsRestorablePreferenceKey("Onboarding.Pending"), Is.False);
			Assert.That(BackupComponentGroups.IsRestorablePreferenceKey("appearance.themeMode"), Is.True);
			Assert.That(BackupComponentGroups.IsRestorablePreferenceKey("lock.clientLockScreen"), Is.True);
		});
	}

	[Test]
	public void The_rating_prompt_history_is_never_restored()
	{
		Assert.That(BackupComponentGroups.IsRestorablePreferenceKey("storeRatingPrompt.history"), Is.False);
	}

	[Test]
	public void The_github_star_prompt_state_is_never_restored()
	{
		Assert.That(BackupComponentGroups.IsRestorablePreferenceKey("githubStarPrompt.shownAt"), Is.False);
	}

	[Test]
	public void The_last_seen_announcement_is_never_restored()
	{
		Assert.That(BackupComponentGroups.IsRestorablePreferenceKey("announcements.lastSeenNumber"), Is.False);
	}

	[Test]
	public void Restoring_the_key_ring_alone_warns_about_the_keys_it_leaves_behind()
	{
		var plan = ExpandAlone(BackupComponentGroup.Integrations);

		Assert.That(plan.Warnings,
			Is.SupersetOf(new[]
			{
				new BackupDependencyWarning(BackupComponentGroup.Integrations, BackupComponentGroup.AppSettings),
				new BackupDependencyWarning(BackupComponentGroup.Integrations, BackupComponentGroup.Accounts)
			}));
	}

	[TestCase(BackupComponentGroup.AppSettings)]
	[TestCase(BackupComponentGroup.Accounts)]
	public void Restoring_keys_without_the_key_ring_that_protects_them_warns(BackupComponentGroup group)
	{
		var plan = ExpandAlone(group);

		Assert.That(plan.Warnings,
			Does.Contain(new BackupDependencyWarning(group, BackupComponentGroup.Integrations)));
	}

	[Test]
	public void Selecting_any_key_group_brings_the_other_two_along_by_default()
	{
		Assert.Multiple(() =>
		{
			Assert.That(BackupComponentSelection.Expand([BackupComponentGroup.Integrations]).Effective,
				Is.SupersetOf(new[] { BackupComponentGroup.AppSettings, BackupComponentGroup.Accounts }));
			Assert.That(BackupComponentSelection.Expand([BackupComponentGroup.AppSettings]).Effective,
				Is.SupersetOf(new[] { BackupComponentGroup.Integrations, BackupComponentGroup.Accounts }));
		});
	}

	[Test]
	public void An_unpublished_write_belongs_to_the_group_of_its_document()
	{
		Assert.Multiple(() =>
		{
			Assert.That(BackupComponentGroups.Owner("data/user-variables.json.tmp"),
				Is.EqualTo(BackupComponentGroup.Variables));
			Assert.That(BackupComponentGroups.Owner("keys/kek.escrow.tmp"),
				Is.EqualTo(BackupComponentGroup.Integrations));
			Assert.That(BackupComponentGroups.Owner("data/user-variables.json.corrupt-20261028-120000-000"),
				Is.Null,
				"a quarantined copy is never read back and must survive a restore of its group");
		});
	}

	[TestCase("data/user-variables.json.tmp", BackupComponentGroup.Variables)]
	[TestCase("data/profiles/kitchen.json.tmp", BackupComponentGroup.Profiles)]
	[TestCase("data/profiles/kitchen.json.corrupt-20261028-120000-000", BackupComponentGroup.Profiles)]
	public void An_archive_entry_that_is_a_sidecar_is_never_restored(string path, BackupComponentGroup group)
	{
		Assert.That(BackupArchiveLimits.IsRestorableEntry(path, group), Is.False);
	}

	[Test]
	public void The_documents_themselves_stay_restorable()
	{
		Assert.Multiple(() =>
		{
			Assert.That(BackupArchiveLimits.IsRestorableEntry("data/user-variables.json",
					BackupComponentGroup.Variables),
				Is.True);
			Assert.That(BackupArchiveLimits.IsRestorableEntry("data/user-variables.json.bak",
					BackupComponentGroup.Variables),
				Is.True);
		});
	}

	private static BackupSelectionPlan ExpandAlone(BackupComponentGroup group)
	{
		return BackupComponentSelection.Expand([group], BackupComponentGroups.AllIds.Except([group]).ToList());
	}
}
