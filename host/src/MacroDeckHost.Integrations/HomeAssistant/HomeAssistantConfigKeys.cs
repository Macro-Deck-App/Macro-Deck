using System.Text.Json;

namespace MacroDeckHost.Integrations.HomeAssistant;

internal static class HomeAssistantConfigKeys
{
	public const string BaseUrl = "baseUrl";
	public const string Token = "token";

	// The four keys below are compatibility-only: they were written by the config-flow watched-entity
	// step that HomeAssistantWatchedEntityMigration now migrates away from. Nothing writes them anymore,
	// but an existing install's stored config still carries them until that migration runs, so the keys
	// stay readable rather than being removed.
	public const string WatchedEntities = "watchedEntities";

	public const string WatchedDomains = "watchedDomains";

	public const string WatchedAreas = "watchedAreas";

	public const string WatchedEntityNames = "watchedEntityNames";

	/// <summary>Marker written once <see cref="HomeAssistantWatchedEntityMigration"/> has completed.</summary>
	public const string WatchedEntitiesMigrated = "watchedEntitiesMigrated";

	public static IReadOnlyList<string> ParseEntities(string? stored)
	{
		if (!TryParse(stored, JsonValueKind.Array, out var root))
		{
			return [];
		}

		var entities = new List<string>(root.GetArrayLength());
		foreach (var entry in root.EnumerateArray())
		{
			if (entry.ValueKind == JsonValueKind.String && entry.GetString() is { Length: > 0 } entityId)
			{
				entities.Add(entityId);
			}
		}

		return entities;
	}

	private static bool TryParse(string? stored, JsonValueKind expected, out JsonElement root)
	{
		root = default;
		if (string.IsNullOrWhiteSpace(stored))
		{
			return false;
		}

		try
		{
			using var document = JsonDocument.Parse(stored);
			if (document.RootElement.ValueKind != expected)
			{
				return false;
			}

			root = document.RootElement.Clone();
			return true;
		}
		catch (JsonException)
		{
			return false;
		}
	}
}
