using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace MacroDeckHost.Tests.SystemTests.Support;

// The admin REST surface the desktop UI drives plugins through, read as plain JSON so the assertions
// follow the wire and not the host's own DTO types.
internal static class HostPlugins
{
	public static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);

	public static async Task<JsonNode> InstallAsync(this HostInstance host, string artifactPath)
	{
		using var content = new MultipartFormDataContent();
		var file = new ByteArrayContent(await File.ReadAllBytesAsync(artifactPath));
		file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
		content.Add(file, "file", Path.GetFileName(artifactPath));

		using var response = await host.Api.PostAsync("/api/plugin-installation/install?allowUnsigned=true", content);
		response.EnsureSuccessStatusCode();
		return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
	}

	public static async Task<JsonNode> UninstallAsync(this HostInstance host, string pluginId)
	{
		using var response = await host.Api.DeleteAsync($"/api/plugin-installation/{pluginId}?keepData=false");
		response.EnsureSuccessStatusCode();
		return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
	}

	public static async Task<JsonNode?> RuntimeAsync(this HostInstance host, string pluginId)
	{
		var runtime = await host.GetJsonAsync("/api/plugin-runtime");
		return runtime["plugins"]!.AsArray()
			.FirstOrDefault(plugin => (string?)plugin!["pluginId"] == pluginId);
	}

	public static async Task<JsonNode> WaitForRuntimeAsync(
		this HostInstance host,
		string pluginId,
		Func<JsonNode, bool> condition,
		TimeSpan timeout,
		string because)
	{
		JsonNode? last = null;
		try
		{
			return await Poll.ForAsync(async () =>
				{
					last = await host.RuntimeAsync(pluginId);
					return last is not null && condition(last) ? last : null;
				},
				timeout,
				because);
		}
		catch (TimeoutException exception)
		{
			throw new TimeoutException($"{exception.Message} Last runtime state: {last?.ToJsonString()}", exception);
		}
	}

	public static Task<JsonNode> WaitUntilHealthyAsync(
		this HostInstance host,
		string pluginId,
		int? notProcessId = null)
	{
		return host.WaitForRuntimeAsync(pluginId,
			runtime => (string?)runtime["state"] == "running"
				&& (string?)runtime["health"] == "healthy"
				&& (int?)runtime["processId"] is { } processId
				&& processId != notProcessId,
			StartTimeout,
			$"{pluginId} is running and healthy");
	}

	public static async Task<string> IntegrationIdOfActionAsync(this HostInstance host, string actionId)
	{
		return await Poll.ForAsync(async () =>
			{
				var actions = await host.GetJsonAsync("/api/actions");
				return actions["actions"]!.AsArray()
					.Where(action => (string?)action!["id"] == actionId)
					.Select(action => (string?)action!["integrationId"])
					.FirstOrDefault();
			},
			StartTimeout,
			$"an integration offers the action '{actionId}'");
	}

	// Walks the integration's config flow the way the desktop UI does; an integration that requires
	// configuration offers no actions until an entry exists.
	public static async Task<JsonNode> ConfigureAsync(
		this HostInstance host,
		string integrationId,
		IReadOnlyDictionary<string, string> values)
	{
		using var started = await host.PostAsync($"/api/integrations/{integrationId}/config-flow/start");
		started.EnsureSuccessStatusCode();
		var flow = JsonNode.Parse(await started.Content.ReadAsStringAsync())!;

		using var submitted = await host.PostAsync($"/api/integrations/{integrationId}/config-flow/submit",
			new { flowId = (string?)flow["flowId"], stepId = (string?)flow["step"]!["stepId"], values });
		submitted.EnsureSuccessStatusCode();
		return JsonNode.Parse(await submitted.Content.ReadAsStringAsync())!;
	}

	public static async Task<JsonNode?> IntegrationAsync(this HostInstance host, string integrationId)
	{
		var integrations = await host.GetJsonAsync("/api/integrations");
		return integrations["integrations"]!.AsArray()
			.FirstOrDefault(integration => (string?)integration!["id"] == integrationId);
	}

	public static async Task<JsonNode> RunActionAsync(
		this HostInstance host,
		string integrationId,
		string actionId,
		object parameters)
	{
		using var response = await host.Api.PostAsJsonAsync("/api/actions/run",
			new { integrationId, actionId, parameters });
		response.EnsureSuccessStatusCode();
		return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
	}

	public static async Task<JsonNode?> VariableAsync(this HostInstance host, string name)
	{
		var variables = await host.GetJsonAsync("/api/variables");
		return variables["variables"]!.AsArray().FirstOrDefault(variable => (string?)variable!["name"] == name);
	}
}
