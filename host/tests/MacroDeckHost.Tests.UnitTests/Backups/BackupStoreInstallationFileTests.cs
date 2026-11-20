using MacroDeckHost.Application.Backups;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.Backups;

[TestFixture]
public class BackupStoreInstallationFileTests
{
	[TestCase("store/installations.json")]
	[TestCase("store/installations.json.bak")]
	public void The_Store_installation_records_travel_with_the_profiles(string path)
	{
		Assert.That(BackupComponentGroups.Owner(path), Is.EqualTo(BackupComponentGroup.Profiles));
	}

	[Test]
	public void The_Store_installation_records_are_restored_only_with_everything_they_describe()
	{
		Assert.Multiple(() =>
		{
			Assert.That(BackupComponentGroups.IsRestoredWith("store/installations.json",
					[BackupComponentGroup.Icons, BackupComponentGroup.Plugins, BackupComponentGroup.Profiles]),
				Is.True);
			Assert.That(BackupComponentGroups.IsRestoredWith("store/installations.json",
					[BackupComponentGroup.Plugins, BackupComponentGroup.Profiles]),
				Is.False);
			Assert.That(BackupComponentGroups.IsRestoredWith("data/profiles/office.json",
					[BackupComponentGroup.Plugins, BackupComponentGroup.Profiles]),
				Is.True);
		});
	}

	[TestCase("store/registry/current/catalog.json")]
	[TestCase("store/media/icon.png")]
	[TestCase("store/operations.json")]
	public void Store_caches_and_operation_state_are_not_backed_up(string path)
	{
		Assert.That(BackupComponentGroups.Owner(path), Is.Null);
	}
}
