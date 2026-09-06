using MacroDeck.Sdk.ConfigFlow;
using MacroDeckHost.Domain.Entities;
using DomainVariableType = MacroDeckHost.Domain.Enums.VariableType;

namespace MacroDeckHost.Integrations.HomeAssistant;

/// <summary>
/// One-time migration from the old config-flow watched-entity picker to
/// <see cref="HomeAssistantVariableCatalog"/> bindings.
///
/// <para>
/// Invoked from <see cref="HomeAssistantIntegration.InitializeAsync"/> when the host has handed the
/// integration a binding store through <see cref="IHomeAssistantBindingStoreConsumer"/>:
/// </para>
///
/// <code>
/// if (!await migration.HasRunAsync())
/// {
///     var existing = store.Load();
///     var bindings = await migration.BuildAsync(existing);
///     if (bindings.Count > 0)
///     {
///         store.Save(existing.Concat(bindings));
///     }
///     await migration.MarkCompletedAsync();
/// }
/// </code>
/// </summary>
internal sealed class HomeAssistantWatchedEntityMigration
{
	internal const string MigratedFromId = "homeassistant/watched-entities-v1";

	private const string CompletedMarker = "true";

	private readonly IIntegrationConfig _config;

	public HomeAssistantWatchedEntityMigration(IIntegrationConfig config)
	{
		_config = config;
	}

	/// <summary>
	/// Whether the migration has already run for this integration's config entry - or there is no entry
	/// to migrate, which is equally "nothing left to do".
	/// </summary>
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

	/// <summary>
	/// Produces the binding records that reproduce every variable the old (since-deleted)
	/// watched-entity picker used to create for a previously watched entity, now pointing at the
	/// equivalent <see cref="HomeAssistantVariableCatalog"/> resource ids. Does not persist them and
	/// does not mark the migration complete; call <see cref="MarkCompletedAsync"/> once the caller has
	/// saved them.
	///
	/// <para>
	/// <paramref name="existingBindings"/> is this integration's own dedup key, not an input to be
	/// migrated: a binding already present for a given <c>(IntegrationId, LocalResourceId)</c> pair is
	/// skipped, so a caller that re-runs <see cref="BuildAsync"/> after a crash between saving and
	/// <see cref="MarkCompletedAsync"/> - the only way this can run more than once - gets back only what
	/// is still missing, never a duplicate "-2" twin of a binding the first attempt already saved.
	/// </para>
	/// </summary>
	public async Task<IReadOnlyList<VariableBinding>> BuildAsync(
		IReadOnlyCollection<VariableBinding> existingBindings,
		CancellationToken cancellationToken = default)
	{
		var entryId = await ResolveEntryAsync(cancellationToken);
		if (entryId is not { } id)
		{
			return [];
		}

		var stored = await _config.GetStringAsync(id, HomeAssistantConfigKeys.WatchedEntities, cancellationToken);
		var entityIds = HomeAssistantConfigKeys.ParseEntities(stored);

		// The same call with the same list reproduces the exact names the old code produced - see
		// HomeAssistantVariableNames' remarks. That determinism, not anything read back from storage, is
		// what lets a user's already-bound templates keep resolving after this migration.
		var names = HomeAssistantVariableNames.Build(entityIds);

		var known = new HashSet<(string IntegrationId, string LocalResourceId)>(
			existingBindings.Select(b => (b.IntegrationId, b.LocalResourceId)));

		var now = DateTime.UtcNow;
		var bindings = new List<VariableBinding>(names.Count * 2);

		foreach (var name in names)
		{
			AddIfNew(bindings, known, $"entity/{name.EntityId}/state", name.StateName, now);
			AddIfNew(bindings, known, $"entity/{name.EntityId}/attributes", name.AttributesName, now);
		}

		return bindings;
	}

	private static void AddIfNew(
		List<VariableBinding> bindings,
		HashSet<(string IntegrationId, string LocalResourceId)> known,
		string localResourceId,
		string name,
		DateTime now)
	{
		if (!known.Add((HomeAssistantIntegration.IntegrationId, localResourceId)))
		{
			return;
		}

		bindings.Add(new VariableBinding
		{
			Id = Guid.NewGuid(),
			IntegrationId = HomeAssistantIntegration.IntegrationId,
			LocalResourceId = localResourceId,
			Name = name,
			Type = DomainVariableType.Text,
			CreatedAt = now,
			MigratedFrom = MigratedFromId
		});
	}

	/// <summary>
	/// Marks the migration done and clears the now-unused <see cref="HomeAssistantConfigKeys.WatchedEntities"/>
	/// value. Call only after the bindings from <see cref="BuildAsync"/> have been durably saved - this
	/// is what makes the migration safe to run exactly once.
	/// </summary>
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

	private async Task<Guid?> ResolveEntryAsync(CancellationToken cancellationToken)
	{
		var entries = await _config.GetEntriesAsync(cancellationToken);
		return entries.Count > 0 ? entries[0].Id : null;
	}
}
