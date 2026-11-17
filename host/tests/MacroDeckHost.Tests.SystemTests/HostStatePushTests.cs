using System.Net.Http.Json;
using System.Text.Json.Nodes;
using MacroDeckHost.Tests.SystemTests.Support;

namespace MacroDeckHost.Tests.SystemTests;

[TestFixture]
internal sealed class HostStatePushTests : SystemTestFixture
{
	private const string PluginId = "com.example.host-state-probe";

	[Test]
	public async Task Adding_a_widget_sends_a_connected_plugin_a_widgets_host_state_that_contains_it()
	{
		var host = await StartHostAsync();
		var credential = await RawProtocolPlugin.EnrollAsync(host, PluginId);
		var (status, session) = await RawProtocolPlugin.RequestSessionAsync(host, credential, 1, 3);
		Assert.That(status, Is.EqualTo(201), session.ToJsonString());
		await using var plugin = await RawProtocolPlugin.ConnectAsync(host, session);
		await plugin.HelloAsync();
		var folderId = await CreateProfileRootFolderAsync(host);
		await Poll.UntilAsync(async () =>
			{
				await plugin.PingAsync();
				return plugin.Received.Any(IsWidgetsState);
			},
			TimeSpan.FromSeconds(30),
			"the plugin received the widgets snapshot every session starts with");
		plugin.Received.Clear();

		var widgetId = await CreateWidgetAsync(host, folderId);

		await Poll.UntilAsync(async () =>
			{
				await plugin.PingAsync();
				return plugin.Received.Any(message => IsWidgetsStateWith(message, widgetId));
			},
			TimeSpan.FromSeconds(30),
			"the plugin received a widgets host.state naming the new widget");
	}

	private static bool IsWidgetsState(JsonNode message)
	{
		return (string?)message["type"] == "host.state" && (string?)message["payload"]?["api"] == "widgets";
	}

	private static bool IsWidgetsStateWith(JsonNode message, string widgetId)
	{
		return IsWidgetsState(message)
			&& message["payload"]!["data"]?.ToJsonString().Contains(widgetId, StringComparison.OrdinalIgnoreCase)
			== true;
	}

	private static async Task<string> CreateProfileRootFolderAsync(HostInstance host)
	{
		using var created = await host.PostAsync("/api/profiles",
			new { name = "Host State", defaultColumns = 3, defaultRows = 2 });
		created.EnsureSuccessStatusCode();
		var profileId = (string)JsonNode.Parse(await created.Content.ReadAsStringAsync())!["profile"]!["id"]!;

		var folders = await host.GetJsonAsync($"/api/folders?profileId={profileId}");
		return (string)folders["folders"]!.AsArray()
			.Single(folder => (string?)folder!["profileId"] == profileId && folder!["parentId"] is null)!["id"]!;
	}

	private static async Task<string> CreateWidgetAsync(HostInstance host, string folderId)
	{
		using var response = await host.Api.PostAsJsonAsync("/api/widgets",
			new
			{
				folderId,
				type = "ActionButton",
				positionX = 0,
				positionY = 0,
				width = 1,
				height = 1,
				data = "{}"
			});
		response.EnsureSuccessStatusCode();
		var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
		return (string?)body["widget"]?["id"] ?? throw new InvalidOperationException(body.ToJsonString());
	}
}
