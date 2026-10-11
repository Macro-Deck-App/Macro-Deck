using System.Text.Json;

namespace MacroDeck.Sdk.ConfigFlow;

/// <summary>
/// One configuration entry as Macro Deck stores it, handed to an <see cref="IConfigEntryLifecycle" />.
/// <see cref="Values" /> holds what the config flow produced; a secret field holds a reference to the
/// encrypted value rather than the value, so read secrets with
/// <see cref="IIntegrationConfig.GetSecretAsync" />.
/// </summary>
public sealed record StoredConfigEntry
{
	public required Guid Id { get; init; }

	public required string Title { get; init; }

	public DateTime CreatedAt { get; init; }

	public IReadOnlyDictionary<string, JsonElement> Values { get; init; } =
		new Dictionary<string, JsonElement>(StringComparer.Ordinal);
}
