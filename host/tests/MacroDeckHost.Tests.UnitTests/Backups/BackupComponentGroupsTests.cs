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
			Assert.That(BackupComponentGroups.IsRestorablePreferenceKey("Onboarding.Pending"), Is.False);
			Assert.That(BackupComponentGroups.IsRestorablePreferenceKey("appearance.themeMode"), Is.True);
			Assert.That(BackupComponentGroups.IsRestorablePreferenceKey("lock.clientLockScreen"), Is.True);
		});
	}
}
