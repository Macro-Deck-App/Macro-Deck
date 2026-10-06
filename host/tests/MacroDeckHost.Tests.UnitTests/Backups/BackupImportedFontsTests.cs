using MacroDeckHost.Application.Backups;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.Backups;

[TestFixture]
public class BackupImportedFontsTests
{
	[Test]
	public void Imported_fonts_travel_with_the_icons_that_profiles_already_depend_on()
	{
		Assert.Multiple(() =>
		{
			Assert.That(BackupComponentGroups.Owner("data/fonts/0123456789abcdef.ttf"), Is.EqualTo(BackupComponentGroup.Icons));
			Assert.That(BackupComponentGroups.Definition(BackupComponentGroup.Profiles).Requires,
				Does.Contain(BackupComponentGroup.Icons));
		});
	}
}
