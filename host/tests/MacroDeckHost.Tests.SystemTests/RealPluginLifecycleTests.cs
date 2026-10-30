using System.Globalization;
using MacroDeck.Plugin.Testing;
using MacroDeckHost.Tests.SystemTests.Support;

namespace MacroDeckHost.Tests.SystemTests;

// SYS-PLG-01: the host process and a plugin process built from the SDK, both real, talking over the real
// listener: what the contract tests only ever see through an in-memory link.
[TestFixture]
internal sealed class RealPluginLifecycleTests : SystemTestFixture
{
	private const string PluginId = FixtureArtifacts.WellBehavedPluginId;
	private const string ThresholdVariable = "sample_alert_threshold_celsius";

	[Test]
	public async Task An_installed_plugin_runs_survives_a_host_restart_and_uninstalls_without_leftovers()
	{
		var host = await StartHostAsync();

		var installed = await host.InstallAsync(FixtureArtifacts.WellBehaved(ArtifactsDirectory));
		Assert.That((bool?)installed["success"], Is.True, installed.ToJsonString());

		var running = await host.WaitUntilHealthyAsync(PluginId);
		var firstProcessId = (int)running["processId"]!;

		var configured = await host.ConfigureAsync(PluginId,
			new Dictionary<string, string> { ["location"] = "Vienna, Austria" });
		Assert.That((string?)configured["kind"], Is.EqualTo("complete").IgnoreCase, configured.ToJsonString());

		var integrationId = await host.IntegrationIdOfActionAsync("set-alert-threshold");
		Assert.That(integrationId, Is.EqualTo(PluginId));
		await SetThresholdAndObserveAsync(host, integrationId, 12);

		await host.RestartAsync();
		var afterRestart = await host.WaitUntilHealthyAsync(PluginId, firstProcessId);
		await SetThresholdAndObserveAsync(host, integrationId, 17);

		var secondProcessId = (int)afterRestart["processId"]!;
		var uninstalled = await host.UninstallAsync(PluginId);
		await Wait.UntilAsync(() => HasExited(secondProcessId), TimeSpan.FromSeconds(30), "the plugin process ended");

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(host.Starts, Is.EqualTo(2), "the restart relaunched the host process");
			Assert.That((bool?)uninstalled["success"], Is.True, uninstalled.ToJsonString());
			Assert.That(HasExited(firstProcessId), Is.True, "the pre-restart plugin process is gone");
			Assert.That(await host.RuntimeAsync(PluginId), Is.Null, "the supervisor forgot the plugin");
			Assert.That(Directory.Exists(Path.Combine(host.PluginsDirectory, PluginId)), Is.False);
			Assert.That(host.Output.Where(line => line.Contains(" ERR]", StringComparison.Ordinal)
					&& line.Contains(PluginId, StringComparison.Ordinal)),
				Is.Empty);
		});
	}

	// The action runs in the plugin process and the variable is read back from it, so a value arriving
	// in the host proves both directions of the live session.
	private static async Task SetThresholdAndObserveAsync(HostInstance host, string integrationId, int threshold)
	{
		var result = await host.RunActionAsync(integrationId,
			"set-alert-threshold",
			new { thresholdCelsius = threshold });
		Assert.That((bool?)result["success"], Is.True, result.ToJsonString());

		var expected = threshold.ToString(CultureInfo.InvariantCulture);
		await Poll.UntilAsync(async () => await host.VariableAsync(ThresholdVariable) is { } variable
				&& (string?)variable["value"] == expected,
			TimeSpan.FromSeconds(60),
			$"{ThresholdVariable} reads {expected}");
	}
}
