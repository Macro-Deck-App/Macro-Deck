using System.Diagnostics;
using System.Text.Json.Nodes;
using MacroDeck.Plugin.Testing;
using MacroDeckHost.Tests.SystemTests.Support;

namespace MacroDeckHost.Tests.SystemTests;

// SYS-PLG-02: the supervisor's guarantees against a real plugin process that misbehaves. The fixture reads
// MACRODECK_MISBEHAVE from its environment, which it inherits from the host that launches it.
[TestFixture]
internal sealed class MisbehavingPluginTests : SystemTestFixture
{
	private const string PluginId = FixtureArtifacts.MisbehavingPluginId;
	private const string MisbehaveVariable = "MACRODECK_MISBEHAVE";

	// The budget the host documents and names in its own failure message: five automatic restarts
	// within the window, the startup-grace retry of the very first early exit not counted.
	private const int DocumentedRestartBudget = 5;

	private static readonly TimeSpan _crashLoopTimeout = TimeSpan.FromMinutes(3);

	private static readonly TimeSpan _severalReconcileTicks = TimeSpan.FromSeconds(6);

	[Test]
	public async Task A_plugin_that_keeps_crashing_fails_after_exactly_the_documented_restart_budget()
	{
		await InstallHealthyAndStopAsync();

		var host = await StartHostAsync(Misbehave("exit-immediately"));
		var failed = await host.WaitForRuntimeAsync(PluginId,
			runtime => (string?)runtime["state"] == "failed",
			_crashLoopTimeout,
			"the crash loop ends in the failed state");

		await Task.Delay(TimeSpan.FromSeconds(3));
		var later = await host.RuntimeAsync(PluginId);

		Assert.Multiple(() =>
		{
			Assert.That((int?)failed["restartCount"], Is.EqualTo(DocumentedRestartBudget), failed.ToJsonString());
			Assert.That((string?)failed["lastError"],
				Does.Contain($"Exceeded {DocumentedRestartBudget} restarts"),
				"the message names the budget that was actually granted");
			Assert.That((string?)later!["state"], Is.EqualTo("failed"), "a failed plugin is not relaunched");
			Assert.That((int?)later["restartCount"], Is.EqualTo(DocumentedRestartBudget));
		});
	}

	[Test]
	public async Task A_plugin_killed_from_outside_is_restarted_by_the_supervisor()
	{
		var host = await StartHostAsync();
		await InstallAsync(host);
		var running = await host.WaitUntilHealthyAsync(PluginId);
		var killedProcessId = (int)running["processId"]!;

		using (var process = Process.GetProcessById(killedProcessId))
		{
			process.Kill(true);
		}

		var restarted = await host.WaitUntilHealthyAsync(PluginId, killedProcessId);

		Assert.Multiple(() =>
		{
			Assert.That((int?)restarted["restartCount"], Is.EqualTo(1));
			Assert.That((string?)restarted["lastStopReason"], Is.EqualTo("crash"));
			Assert.That(HasExited(killedProcessId), Is.True);
		});
	}

	[Test]
	public async Task An_action_of_a_stopped_plugin_fails_promptly_instead_of_hanging()
	{
		var host = await StartHostAsync();
		await InstallAsync(host);
		await host.WaitUntilHealthyAsync(PluginId);
		var integrationId = await host.IntegrationIdOfActionAsync("log-flood");

		using (var stopped = await host.PostAsync($"/api/plugin-runtime/{PluginId}/stop"))
		{
			stopped.EnsureSuccessStatusCode();
		}

		await host.WaitForRuntimeAsync(PluginId,
			runtime => (string?)runtime["state"] == "stopped",
			HostPlugins.StartTimeout,
			"the plugin stopped");

		var clock = Stopwatch.StartNew();
		var result = await host.RunActionAsync(integrationId, "log-flood", new { });
		clock.Stop();

		Assert.Multiple(() =>
		{
			Assert.That((bool?)result["success"], Is.False, result.ToJsonString());
			Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)), "the call must not wait for a timeout");
		});
	}

	[Test]
	public async Task A_plugin_that_hangs_in_shutdown_does_not_hold_the_host_open()
	{
		var host = await StartHostAsync(Misbehave("hang-stop"));
		await InstallAsync(host);
		var running = await host.WaitUntilHealthyAsync(PluginId);
		var pluginProcessId = (int)running["processId"]!;

		var clock = Stopwatch.StartNew();
		await host.StopAsync();
		clock.Stop();

		await Wait.UntilAsync(() => HasExited(pluginProcessId), TimeSpan.FromSeconds(10), "the plugin is gone");
		Assert.Multiple(() =>
		{
			Assert.That(host.ExitedOnItsOwn, Is.True, "the host exited without being killed by the test");
			Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(40)), "the host gave up on the plugin in time");
		});
	}

	[Test]
	public async Task A_launch_that_fails_before_the_process_starts_is_tried_once_until_started_again_by_hand()
	{
		await InstallHealthyAndStopAsync();
		foreach (var manifest in Directory.EnumerateFiles(Path.Combine(State.Path, "data", "plugins", PluginId),
			"manifest.json",
			SearchOption.AllDirectories))
		{
			await File.WriteAllTextAsync(manifest, "{ not a manifest");
		}

		var host = await StartHostAsync();
		await Poll.UntilAsync(() => Task.FromResult(ManifestWarnings(host) >= 1),
			HostPlugins.StartTimeout,
			"the host tried to launch the plugin");
		await Task.Delay(_severalReconcileTicks);
		var afterStartup = await host.RuntimeAsync(PluginId);
		var warningsAfterStartup = ManifestWarnings(host);

		var manualStart = await PostRuntimeAsync(host, "start");
		await Poll.UntilAsync(() => Task.FromResult(ManifestWarnings(host) >= 2),
			TimeSpan.FromSeconds(30),
			"a manual start tries again");
		var warningsAfterManualStart = ManifestWarnings(host);
		var stopped = await PostRuntimeAsync(host, "stop");
		await host.RestartAsync();
		await Task.Delay(_severalReconcileTicks);

		Assert.Multiple(() =>
		{
			Assert.That(warningsAfterStartup, Is.EqualTo(1), "a failed launch is not retried on every tick");
			Assert.That((string?)afterStartup!["state"], Is.Not.EqualTo("running"), afterStartup.ToJsonString());
			Assert.That((bool?)manualStart["success"], Is.False, manualStart.ToJsonString());
			Assert.That(warningsAfterManualStart, Is.EqualTo(2), "a manual start tries again");
			Assert.That((bool?)stopped["success"], Is.True, stopped.ToJsonString());
			Assert.That(ManifestWarnings(host), Is.EqualTo(2), "a stopped plugin is not launched on the next start");
		});
	}

	private static int ManifestWarnings(HostInstance host)
	{
		return host.Output.Count(line => line.Contains($"The manifest of plugin '{PluginId}' could not be read",
			StringComparison.Ordinal));
	}

	private static async Task<JsonNode> PostRuntimeAsync(HostInstance host, string operation)
	{
		using var response = await host.PostAsync($"/api/plugin-runtime/{PluginId}/{operation}");
		response.EnsureSuccessStatusCode();
		return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
	}

	private static Dictionary<string, string?> Misbehave(string behaviour)
	{
		return new Dictionary<string, string?> { [MisbehaveVariable] = behaviour };
	}

	private async Task InstallAsync(HostInstance host)
	{
		var installed = await host.InstallAsync(FixtureArtifacts.Misbehaving(ArtifactsDirectory));
		Assert.That((bool?)installed["success"], Is.True, installed.ToJsonString());
	}

	// Installed while it behaves, because an install that never turns healthy is rolled back; the crash
	// loop then starts from the next host start, with the plugin's desired state still "running".
	private async Task InstallHealthyAndStopAsync()
	{
		var host = await StartHostAsync();
		await InstallAsync(host);
		await host.WaitUntilHealthyAsync(PluginId);
		await host.StopAsync();
	}
}
