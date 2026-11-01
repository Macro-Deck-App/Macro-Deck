using MacroDeckHost.Infrastructure.Plugins.Installation;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Plugins.Installation;

[TestFixture]
internal sealed class PluginStagingSweeperTests
{
	[Test]
	public void An_interrupted_install_is_removed_and_the_staging_folder_stays()
	{
		var paths = new TestPaths();
		var leftover = Path.Combine(paths.PluginStagingDirectory, "0123456789abcdef", "extract");
		Directory.CreateDirectory(leftover);
		File.WriteAllText(Path.Combine(leftover, "plugin.dll"), "binary");
		File.WriteAllText(Path.Combine(paths.PluginStagingDirectory, "stray"), "x");

		PluginStagingSweeper.Sweep(paths);

		Assert.Multiple(() =>
		{
			Assert.That(Directory.Exists(paths.PluginStagingDirectory), Is.True);
			Assert.That(Directory.GetFileSystemEntries(paths.PluginStagingDirectory), Is.Empty);
		});
	}

	[Test]
	public void A_host_without_a_staging_folder_starts_normally()
	{
		var paths = new TestPaths();
		if (Directory.Exists(paths.PluginStagingDirectory))
		{
			Directory.Delete(paths.PluginStagingDirectory, true);
		}

		Assert.DoesNotThrow(() => PluginStagingSweeper.Sweep(paths));
	}
}
