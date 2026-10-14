using System.Text.Json;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Integrations;

internal static class ConfigurationIdentityPreparation
{
	public static string? ChooseKey(
		ConfigEntryPreparation preparation,
		Func<StoredConfigEntry, ConfigurationIdentity?> readIdentity,
		Func<string, IReadOnlyList<VariableDefinition>> declare,
		Func<int>? suffixFactory)
	{
		var rejected = preparation.RejectedVariableNames.ToHashSet(StringComparer.Ordinal);

		bool IsRejected(string key)
		{
			return declare(key).Any(variable => variable.Name is { } name && rejected.Contains(name));
		}

		var current = preparation.Existing is null ? null : readIdentity(preparation.Existing)?.Key;
		if (current is not null && !preparation.TitleChanged)
		{
			return IsRejected(current) ? null : current;
		}

		var occupied = preparation.Siblings
			.Select(readIdentity)
			.OfType<ConfigurationIdentity>()
			.Select(identity => identity.Key)
			.ToHashSet(StringComparer.Ordinal);
		for (var attempt = 0; attempt < ConfigurationIdentityAllocator.MaximumAttempts; attempt++)
		{
			var candidate = ConfigurationIdentityAllocator.Allocate(preparation.Title,
				occupied,
				preparation.TitleChanged ? current : null,
				suffixFactory);
			if (candidate is null)
			{
				return null;
			}

			if (!IsRejected(candidate))
			{
				return candidate;
			}

			occupied.Add(candidate);
		}

		return null;
	}

	public static ConfigurationIdentity? ReadIdentity(StoredConfigEntry entry, string identityKey)
	{
		return entry.Values.TryGetValue(identityKey, out var identity)
			&& ConfigurationIdentityJson.TryParse(ReadString(identity), out var parsed)
				? parsed
				: null;
	}

	public static bool HasSchema(StoredConfigEntry entry, string schemaKey, string schemaVersion)
	{
		return entry.Values.TryGetValue(schemaKey, out var schema)
			&& string.Equals(ReadString(schema), schemaVersion, StringComparison.Ordinal);
	}

	public static async Task SynchronizeAsync(
		IVariableApi variables,
		IReadOnlyList<(StoredConfigEntry Entry, ConfigurationIdentity Identity)> usable,
		Func<string?, Guid?> entryIdOf,
		Func<string, Guid, string, IReadOnlyList<VariableDefinition>> declare,
		CancellationToken cancellationToken)
	{
		var usableIds = usable.Select(item => item.Entry.Id).ToHashSet();
		foreach (var variable in await variables.GetAllAsync().ConfigureAwait(false))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (entryIdOf(variable.DefinitionId) is { } entryId && !usableIds.Contains(entryId))
			{
				await variables.DeleteAsync(variable.Id).ConfigureAwait(false);
			}
		}

		foreach (var (entry, identity) in usable)
		{
			foreach (var declared in declare(identity.Key, entry.Id, entry.Title))
			{
				cancellationToken.ThrowIfCancellationRequested();
				await variables.CreateAsync(declared).ConfigureAwait(false);
			}
		}
	}

	private static string? ReadString(JsonElement element)
	{
		return element.ValueKind == JsonValueKind.String ? element.GetString() : element.GetRawText();
	}
}
