using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Integrations.HomeAssistant.Configuration;
using MacroDeckHost.Integrations.HomeAssistant.Variables;

namespace MacroDeckHost.Integrations.HomeAssistant.Migration;

internal sealed class HomeAssistantWatchedEntityMigration
{
	internal const string MigratedFromId = "homeassistant/watched-entities-v1";

	private const string CompletedMarker = "true";

	private readonly IIntegrationConfig _config;

	public HomeAssistantWatchedEntityMigration(IIntegrationConfig config)
	{
		_config = config;
	}

	public async Task<bool> HasRunAsync(CancellationToken cancellationToken = default)
	{
		var entryId = await ResolveEntryAsync(cancellationToken);
		if (entryId is not { } id)
		{
			return true;
		}

		var marker = await _config.GetStringAsync(id,
			HomeAssistantConfigKeys.WatchedEntitiesMigrated,
			cancellationToken);
		return marker == CompletedMarker;
	}

	// Rebuilding the names from the same stored list reproduces what the removed watched-entity picker
	// created, so templates that reference them keep resolving. The host skips resources already bound.
	public async Task<IReadOnlyList<VariableBindingRequest>> BuildAsync(CancellationToken cancellationToken = default)
	{
		var entryId = await ResolveEntryAsync(cancellationToken);
		if (entryId is not { } id)
		{
			return [];
		}

		var stored = await _config.GetStringAsync(id, HomeAssistantConfigKeys.WatchedEntities, cancellationToken);
		var names = HomeAssistantVariableNames.Build(HomeAssistantConfigKeys.ParseEntities(stored));

		var requests = new List<VariableBindingRequest>(names.Count * 2);
		foreach (var name in names)
		{
			requests.Add(Request($"entity/{name.EntityId}/state", name.StateName));
			requests.Add(Request($"entity/{name.EntityId}/attributes", name.AttributesName));
		}

		return requests;
	}

	public async Task MarkCompletedAsync(CancellationToken cancellationToken = default)
	{
		var entryId = await ResolveEntryAsync(cancellationToken);
		if (entryId is not { } id)
		{
			return;
		}

		await _config.SetStringAsync(id,
			HomeAssistantConfigKeys.WatchedEntitiesMigrated,
			CompletedMarker,
			cancellationToken);
		await _config.SetStringAsync(id, HomeAssistantConfigKeys.WatchedEntities, null, cancellationToken);
	}

	private static VariableBindingRequest Request(string localResourceId, string name)
	{
		return new VariableBindingRequest(localResourceId, name, VariableType.Text) { MigratedFrom = MigratedFromId };
	}

	private async Task<Guid?> ResolveEntryAsync(CancellationToken cancellationToken)
	{
		var entries = await _config.GetEntriesAsync(cancellationToken);
		return entries.Count > 0 ? entries[0].Id : null;
	}
}
