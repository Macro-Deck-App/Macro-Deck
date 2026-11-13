using MacroDeck.Plugin.Testing;
using MacroDeckHost.Tests.SystemTests.Support;

namespace MacroDeckHost.Tests.SystemTests;

[TestFixture]
internal sealed class PluginsDisabledAtStartupTests : SystemTestFixture
{
	private const string PluginId = FixtureArtifacts.MisbehavingPluginId;

	private static readonly TimeSpan _severalReconcileTicks = TimeSpan.FromSeconds(6);
	private static readonly TimeSpan _commandTimeout = TimeSpan.FromSeconds(60);

	private static readonly string[] _disablePlugins = ["--disable-plugins"];

	[Test]
	public async Task A_session_started_with_plugins_disabled_starts_nothing_and_changes_nothing_for_the_next_start()
	{
		var setup = await InstallHealthyAndStopAsync();
		var savedState = await File.ReadAllBytesAsync(RuntimeStateFile(setup));

		var safe = await StartHostAsync(arguments: _disablePlugins);
		await Task.Delay(_severalReconcileTicks);
		var idle = await safe.RuntimeAsync(PluginId);
		var session = await safe.GetJsonAsync("/api/host/session");
		using (var started = await safe.PostAsync($"/api/plugin-runtime/{PluginId}/start"))
		{
			started.EnsureSuccessStatusCode();
		}

		await safe.WaitUntilHealthyAsync(PluginId);
		using (var stopped = await safe.PostAsync($"/api/plugin-runtime/{PluginId}/stop"))
		{
			stopped.EnsureSuccessStatusCode();
		}

		await safe.WaitForRuntimeAsync(PluginId,
			runtime => (string?)runtime["state"] == "stopped",
			HostPlugins.StartTimeout,
			"the manually started plugin stopped");
		await safe.StopAsync();
		var stateAfterSafeMode = await File.ReadAllBytesAsync(RuntimeStateFile(safe));

		var normal = await StartHostAsync();
		var autoStarted = await normal.WaitUntilHealthyAsync(PluginId);

		Assert.Multiple(() =>
		{
			Assert.That((string?)idle!["state"], Is.EqualTo("stopped"), "nothing starts a plugin automatically");
			Assert.That((bool?)session["pluginsDisabledAtStartup"], Is.True);
			Assert.That(stateAfterSafeMode, Is.EqualTo(savedState), "the session left the saved auto-start alone");
			Assert.That((string?)autoStarted["state"], Is.EqualTo("running"), "a normal start auto-starts again");
		});
	}

	[Test]
	public async Task The_information_options_answer_without_starting_the_host()
	{
		var host = await InstallHealthyAndStopAsync();
		var databaseExisted = File.Exists(Path.Combine(host.DataDirectory, "database.db"));
		var logBytes = LogBytes(host);

		var (versionExit, version) = await host.RunCommandAsync("--version", _commandTimeout);
		var (listExit, list) = await host.RunCommandAsync("--list-plugins", _commandTimeout);
		var (helpExit, help) = await host.RunCommandAsync("--help", _commandTimeout);

		Assert.Multiple(() =>
		{
			Assert.That(databaseExisted, Is.True);
			Assert.That(versionExit, Is.Zero, string.Join('\n', version));
			Assert.That(version[0], Does.StartWith("Macro Deck "));
			Assert.That(listExit, Is.Zero, string.Join('\n', list));
			Assert.That(list, Does.Contain(PluginId));
			Assert.That(helpExit, Is.Zero, string.Join('\n', help));
			Assert.That(string.Join('\n', help), Does.Contain("--disable-plugins"));
			Assert.That(LogBytes(host), Is.EqualTo(logBytes), "no host started and logged");
		});
	}

	private static long LogBytes(HostInstance host)
	{
		return Directory.GetFiles(Path.Combine(host.DataDirectory, "logs"))
			.Sum(file => new FileInfo(file).Length);
	}

	private static string RuntimeStateFile(HostInstance host)
	{
		return Path.Combine(host.DataDirectory, "config", "plugin-runtime.json");
	}

	private async Task<HostInstance> InstallHealthyAndStopAsync()
	{
		var host = await StartHostAsync();
		var installed = await host.InstallAsync(FixtureArtifacts.Misbehaving(ArtifactsDirectory));
		Assert.That((bool?)installed["success"], Is.True, installed.ToJsonString());
		await host.WaitUntilHealthyAsync(PluginId);
		await host.StopAsync();
		return host;
	}
}
