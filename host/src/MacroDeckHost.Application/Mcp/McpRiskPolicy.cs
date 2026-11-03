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
	Task<McpRiskMode> Classify(Guid? tokenId, string integrationId, string actionId);

	Task<IReadOnlyList<McpProviderPolicy>> Providers();

	Task SetOverride(string integrationId, McpRiskMode? mode);

	Task<IReadOnlyList<McpActionRef>> Allowances(Guid tokenId);

	Task Allow(Guid tokenId, IReadOnlyList<McpActionRef> actions);

	Task<bool> RemoveAllowance(Guid tokenId, McpActionRef action);

	Task ClearAllowances(Guid tokenId);
}

public sealed class McpRiskPolicy : IMcpRiskPolicy
{
	public const string PreferenceKey = "mcp.riskPolicy";
	public const string AllowancesPreferenceKey = "mcp.tokenAllowances";

	private readonly IAppPreferenceRepository _preferences;
	private readonly IIntegrationRegistry _registry;

	public McpRiskPolicy(IAppPreferenceRepository preferences, IIntegrationRegistry registry)
	{
		_preferences = preferences;
		_registry = registry;
	}

	public async Task<McpRiskMode> Classify(Guid? tokenId, string integrationId, string actionId)
	{
		var overrides = await Overrides();
		if (overrides is null)
		{
			return McpRiskMode.Off;
		}

		var hasOverride = overrides.TryGetValue(integrationId, out var mode);
		if (hasOverride && mode == McpRiskMode.Off)
		{
			return McpRiskMode.Off;
		}

		if (tokenId is { } id && (await Allowances(id)).Contains(new McpActionRef(integrationId, actionId)))
		{
			return McpRiskMode.Allow;
		}

		return hasOverride ? mode : DefaultFor(integrationId, actionId);
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

	public async Task<IReadOnlyList<McpActionRef>> Allowances(Guid tokenId)
	{
		return (await AllAllowances()).GetValueOrDefault(tokenId) ?? [];
	}

	public async Task Allow(Guid tokenId, IReadOnlyList<McpActionRef> actions)
	{
		var all = await AllAllowances();
		all[tokenId] = (all.GetValueOrDefault(tokenId) ?? [])
			.Concat(actions)
			.Distinct()
			.OrderBy(action => action.IntegrationId, StringComparer.Ordinal)
			.ThenBy(action => action.ActionId, StringComparer.Ordinal)
			.ToList();
		await SaveAllowances(all);
	}

	public async Task<bool> RemoveAllowance(Guid tokenId, McpActionRef action)
	{
		var all = await AllAllowances();
		if (all.GetValueOrDefault(tokenId) is not { } allowed || !allowed.Contains(action))
		{
			return false;
		}

		all[tokenId] = allowed.Where(candidate => candidate != action).ToList();
		await SaveAllowances(all);
		return true;
	}

	public async Task ClearAllowances(Guid tokenId)
	{
		var all = await AllAllowances();
		if (all.Remove(tokenId))
		{
			await SaveAllowances(all);
		}
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

	// Unreadable allowances fall back to none: every action asks again, nothing is allowed by accident.
	private async Task<Dictionary<Guid, List<McpActionRef>>> AllAllowances()
	{
		var raw = (await _preferences.GetByKey(AllowancesPreferenceKey))?.Value;
		if (string.IsNullOrWhiteSpace(raw))
		{
			return [];
		}

		try
		{
			return JsonSerializer.Deserialize<Dictionary<Guid, List<McpActionRef>>>(raw) ?? [];
		}
		catch (JsonException)
		{
			return [];
		}
	}

	private Task SaveAllowances(Dictionary<Guid, List<McpActionRef>> all)
	{
		return _preferences.SetValue(AllowancesPreferenceKey,
			JsonSerializer.Serialize(all.Where(entry => entry.Value.Count > 0)
				.ToDictionary(entry => entry.Key, entry => entry.Value)));
	}
}
