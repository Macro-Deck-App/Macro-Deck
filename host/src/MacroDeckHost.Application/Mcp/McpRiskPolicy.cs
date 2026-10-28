using System.Text.Json;
using System.Text.Json.Serialization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Persistence.Repositories;

namespace MacroDeckHost.Application.Mcp;

[JsonConverter(typeof(JsonStringEnumConverter<McpRiskMode>))]
public enum McpRiskMode
{
	Allow,
	Confirm,
	Off
}

public sealed record McpProviderPolicy(
	string IntegrationId,
	McpRiskMode DefaultMode,
	McpRiskMode? Override,
	IReadOnlyList<string> ConfirmedActions);

public interface IMcpRiskPolicy
{
	Task<McpRiskMode> Classify(string integrationId, string actionId);

	Task<IReadOnlyList<McpProviderPolicy>> Providers();

	Task SetOverride(string integrationId, McpRiskMode? mode);
}

public sealed class McpRiskPolicy : IMcpRiskPolicy
{
	public const string PreferenceKey = "mcp.riskPolicy";

	private readonly IAppPreferenceRepository _preferences;
	private readonly IIntegrationRegistry _registry;

	public McpRiskPolicy(IAppPreferenceRepository preferences, IIntegrationRegistry registry)
	{
		_preferences = preferences;
		_registry = registry;
	}

	public async Task<McpRiskMode> Classify(string integrationId, string actionId)
	{
		var overrides = await Overrides();
		if (overrides is null)
		{
			return McpRiskMode.Off;
		}

		return overrides.TryGetValue(integrationId, out var mode) ? mode : DefaultFor(integrationId, actionId);
	}

	public async Task<IReadOnlyList<McpProviderPolicy>> Providers()
	{
		var overrides = await Overrides() ?? [];
		return _registry.GetActions(false)
			.GroupBy(action => action.OwnerId, StringComparer.Ordinal)
			.Select(group => new McpProviderPolicy(group.Key,
				ProviderDefault(group.Key),
				overrides.TryGetValue(group.Key, out var mode) ? mode : null,
				group.Select(action => action.LocalId)
					.Where(actionId => DefaultFor(group.Key, actionId) == McpRiskMode.Confirm)
					.Order(StringComparer.Ordinal)
					.ToList()))
			.OrderBy(policy => policy.IntegrationId, StringComparer.Ordinal)
			.ToList();
	}

	public async Task SetOverride(string integrationId, McpRiskMode? mode)
	{
		var overrides = await Overrides() ?? new Dictionary<string, McpRiskMode>(StringComparer.Ordinal);
		if (mode is { } value)
		{
			overrides[integrationId] = value;
		}
		else
		{
			overrides.Remove(integrationId);
		}

		await _preferences.SetValue(PreferenceKey, JsonSerializer.Serialize(overrides));
	}

	// Built-in actions declare what they can do (IActionDefinition.RiskCategory); plugin, unknown and
	// offline providers ask first whatever they declare, so a missing declaration never allows anything.
	private McpRiskMode DefaultFor(string integrationId, string actionId)
	{
		if (ProviderDefault(integrationId) == McpRiskMode.Confirm)
		{
			return McpRiskMode.Confirm;
		}

		return _registry.FindAction(integrationId, actionId) is { RiskCategory: ActionRiskCategory.None }
			? McpRiskMode.Allow
			: McpRiskMode.Confirm;
	}

	private McpRiskMode ProviderDefault(string integrationId)
	{
		return _registry.TryGetOrigin(integrationId, out var origin) && origin == IntegrationOrigin.BuiltIn
			? McpRiskMode.Allow
			: McpRiskMode.Confirm;
	}

	// An unreadable policy turns every provider off: it may have held an Off the host can no longer see.
	private async Task<Dictionary<string, McpRiskMode>?> Overrides()
	{
		var raw = (await _preferences.GetByKey(PreferenceKey))?.Value;
		if (string.IsNullOrWhiteSpace(raw))
		{
			return new Dictionary<string, McpRiskMode>(StringComparer.Ordinal);
		}

		try
		{
			return new Dictionary<string, McpRiskMode>(
				JsonSerializer.Deserialize<Dictionary<string, McpRiskMode>>(raw) ?? [],
				StringComparer.Ordinal);
		}
		catch (JsonException)
		{
			return null;
		}
	}
}
