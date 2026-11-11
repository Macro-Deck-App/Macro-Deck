using System.Security.Cryptography;
using MacroDeckHost.Tests.SystemTests.Support;

namespace MacroDeckHost.Tests.SystemTests;

[TestFixture]
internal sealed class SingleInstanceTests : SystemTestFixture
{
	private const int AnotherInstanceRunningExitCode = 89;

	[Test]
	public async Task A_second_host_on_the_same_data_directory_exits_and_leaves_the_running_host_and_its_data_alone()
	{
		var host = await StartHostAsync();
		await IncludedIconPackSyncedAsync(host);
		var filesBefore = await SettledDataFilesAsync(host.DataDirectory);
		var configBefore = ConfigHashes(host.DataDirectory);

		var (exitCode, output) = await host.RunAnotherInstanceAsync(TimeSpan.FromSeconds(60));

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(exitCode, Is.EqualTo(AnotherInstanceRunningExitCode), string.Join(Environment.NewLine, output));
			Assert.That(DataFiles(host.DataDirectory), Is.EquivalentTo(filesBefore), "no file was added or removed");
			Assert.That(ConfigHashes(host.DataDirectory), Is.EqualTo(configBefore), "no configuration file changed");
			using var response = await host.Api.GetAsync("/api/plugin-runtime");
			Assert.That(response.IsSuccessStatusCode, Is.True, "the first host still serves");
		});
	}

	// The included icon pack is seeded in bursts that can pause longer than the settle window.
	private static async Task IncludedIconPackSyncedAsync(HostInstance host)
	{
		await Poll.UntilAsync(() => Task.FromResult(host.Output.Any(EndsIncludedIconPackSync)),
			TimeSpan.FromSeconds(120),
			"the first host finished seeding the included icon pack");
	}

	private static bool EndsIncludedIconPackSync(string line)
	{
		return line.Contains("Synced the included icon pack", StringComparison.Ordinal)
			|| line.Contains("Failed to sync the included icon pack", StringComparison.Ordinal);
	}

	// A freshly started host still writes its first store registry sync, which no second instance causes.
	private static async Task<List<string>> SettledDataFilesAsync(string dataDirectory)
	{
		var previous = DataFiles(dataDirectory);
		return await Poll.ForAsync(async () =>
			{
				await Task.Delay(TimeSpan.FromSeconds(2));
				var current = DataFiles(dataDirectory);
				var settled = current.SequenceEqual(previous) ? current : null;
				previous = current;
				return settled;
			},
			TimeSpan.FromSeconds(60),
			"the first host stopped adding files");
	}

	// The log directory is shared on purpose: the second host logs why it leaves before it does.
	private static List<string> DataFiles(string dataDirectory)
	{
		return Directory.EnumerateFiles(dataDirectory, "*", SearchOption.AllDirectories)
			.Select(path => Path.GetRelativePath(dataDirectory, path))
			.Where(path => !path.StartsWith("logs" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
			.Order(StringComparer.Ordinal)
			.ToList();
	}

	private static Dictionary<string, string> ConfigHashes(string dataDirectory)
	{
		var config = Path.Combine(dataDirectory, "config");
		return Directory.EnumerateFiles(config, "*", SearchOption.AllDirectories)
			.ToDictionary(path => Path.GetRelativePath(config, path),
				path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
	}
}
