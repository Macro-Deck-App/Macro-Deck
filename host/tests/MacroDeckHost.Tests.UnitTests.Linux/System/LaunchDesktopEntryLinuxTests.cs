using System.Runtime.Versioning;
using MacroDeckHost.Integrations.System.Application;

namespace MacroDeckHost.Tests.UnitTests.Linux.System;

[Platform("Linux")]
[SupportedOSPlatform("linux")]
public class LaunchDesktopEntryLinuxTests
{
	[Test]
	public void A_bare_command_is_looked_up_on_PATH()
	{
		Assert.That(LinuxApplicationService.RequiresPathLookup("firefox"), Is.True);
	}

	[Test]
	public void Anything_naming_a_location_is_launched_as_a_path()
	{
		Assert.Multiple(() =>
		{
			Assert.That(LinuxApplicationService.RequiresPathLookup("/usr/bin/firefox"), Is.False);
			Assert.That(LinuxApplicationService.RequiresPathLookup("/opt/gone/app"), Is.False);
			Assert.That(LinuxApplicationService.RequiresPathLookup("./run.sh"), Is.False);
			Assert.That(LinuxApplicationService.RequiresPathLookup("   "), Is.False);
		});
	}

	[Test]
	public void An_existing_file_in_the_working_directory_is_not_a_command()
	{
		var file = Path.Combine(Directory.GetCurrentDirectory(), $"md-{Guid.NewGuid():N}");
		File.WriteAllText(file, string.Empty);
		try
		{
			Assert.That(LinuxApplicationService.RequiresPathLookup(Path.GetFileName(file)), Is.False);
		}
		finally
		{
			File.Delete(file);
		}
	}
}
