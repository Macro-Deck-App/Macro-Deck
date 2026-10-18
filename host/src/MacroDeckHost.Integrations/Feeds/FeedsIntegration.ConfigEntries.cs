using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Integrations.Feeds.Configuration;
using MacroDeckHost.Integrations.Feeds.State;
using MacroDeckHost.Integrations.Feeds.Variables;

namespace MacroDeckHost.Integrations.Feeds;

public sealed partial class FeedsIntegration
{
	private readonly ConcurrentDictionary<Guid, string> _pendingKeys = new();

	public Task<ConfigEntryPreparationResult> PrepareEntryAsync(
		ConfigEntryPreparation preparation,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(preparation);

		var values = new Dictionary<string, JsonElement>(preparation.Values, StringComparer.Ordinal);

		var key = preparation.Existing is { } existing ? ReadKey(existing) : null;
		if (key is null)
		{
			var occupied = preparation.Siblings
				.Where(sibling => sibling.Id != preparation.EntryId)
				.Select(ReadKey)
				.OfType<string>()
				.Concat(_pendingKeys.Where(pair => pair.Key != preparation.EntryId).Select(pair => pair.Value))
				.ToHashSet(StringComparer.Ordinal);
			key = FeedVariables.UniqueKey(preparation.Title, occupied);
		}

		_pendingKeys[preparation.EntryId] = key;
		values[FeedConfigKeys.VariableKey] = JsonSerializer.SerializeToElement(key);
		return Task.FromResult(ConfigEntryPreparationResult.Accept(values));
	}

	public Task<ConfigEntryStatus> GetEntryStatusAsync(
		StoredConfigEntry entry,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(entry);

		return Task.FromResult(_hub.Find(entry.Id)?.Status switch
		{
			null => ConfigEntryStatus.Disconnected,
			FeedStatus.Ok or FeedStatus.NotModified => ConfigEntryStatus.Connected,
			FeedStatus.Pending => ConfigEntryStatus.Connecting,
			_ => ConfigEntryStatus.Disconnected
		});
	}

	public async Task<bool> ReloadEntriesAsync(CancellationToken cancellationToken = default)
	{
		if (IsInitialized)
		{
			await ReloadConfigurationsAsync(cancellationToken).ConfigureAwait(false);
		}

		return true;
	}

	public async Task SynchronizeEntriesAsync(
		IReadOnlyList<StoredConfigEntry> entries,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(entries);

		if (_context is not { } context)
		{
			return;
		}

		var variables = await context.Variables.GetAllAsync().ConfigureAwait(false);
		foreach (var id in OrphanedVariables(variables, entries))
		{
			cancellationToken.ThrowIfCancellationRequested();
			await context.Variables.DeleteAsync(id).ConfigureAwait(false);
		}
	}

	public Task ReleasePreparationAsync(Guid entryId, CancellationToken cancellationToken = default)
	{
		_pendingKeys.TryRemove(entryId, out _);
		return Task.CompletedTask;
	}

	internal static IReadOnlyList<Guid> OrphanedVariables(
		IReadOnlyList<VariableHandle> variables,
		IReadOnlyList<StoredConfigEntry> entries)
	{
		var known = entries.Select(entry => entry.Id).ToHashSet();
		return
		[
			.. variables
				.Where(variable => variable.DefinitionId is { } definitionId
					&& FeedVariables.TryParse(definitionId, out var entryId, out _)
					&& !known.Contains(entryId))
				.Select(variable => variable.Id)
		];
	}

	private static string? ReadKey(StoredConfigEntry entry)
	{
		return entry.Values.TryGetValue(FeedConfigKeys.VariableKey, out var element)
			&& element.ValueKind == JsonValueKind.String
			&& element.GetString() is { Length: > 0 } key
				? key
				: null;
	}
}
