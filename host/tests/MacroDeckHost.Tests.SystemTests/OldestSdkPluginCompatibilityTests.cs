using System.Text.Json.Nodes;
using MacroDeckHost.Tests.SystemTests.Support;

namespace MacroDeckHost.Tests.SystemTests;

[TestFixture]
internal sealed class OldestSdkPluginCompatibilityTests : SystemTestFixture
{
	private const string PluginId = FixtureArtifacts.OldestSdkPluginId;
	private const string VariableName = "oldest_sdk_remembered_text";

	[Test]
	public async Task A_plugin_built_against_the_oldest_supported_sdk_runs_its_action_and_variable_across_a_restart()
	{
		var host = await StartHostAsync();

		var installed = await host.InstallAsync(FixtureArtifacts.OldestSdk(ArtifactsDirectory));
		Assert.That((bool?)installed["success"], Is.True, installed.ToJsonString());
		var running = await host.WaitUntilHealthyAsync(PluginId);

		var integrationId = await host.IntegrationIdOfActionAsync("remember-text");
		Assert.That(integrationId, Is.EqualTo(PluginId));
		await RememberAndObserveAsync(host, integrationId, "before restart");

		await host.RestartAsync();
		await host.WaitUntilHealthyAsync(PluginId, (int)running["processId"]!);
		await RememberAndObserveAsync(host, integrationId, "after restart");
	}

	private static async Task RememberAndObserveAsync(HostInstance host, string integrationId, string text)
	{
		JsonNode? result = null;
		await Poll.UntilAsync(async () =>
			{
				result = await host.RunActionAsync(integrationId, "remember-text", new { text });
				return (string?)result["error"]?["code"] != "CAPABILITY_UNAVAILABLE";
			},
			HostPlugins.StartTimeout,
			"the plugin session attached");
		Assert.That((bool?)result!["success"], Is.True, result.ToJsonString());

		await Poll.UntilAsync(async () => await host.VariableAsync(VariableName) is { } variable
				&& (string?)variable["value"] == text,
			TimeSpan.FromSeconds(60),
			$"{VariableName} reads {text}");
	}
}
