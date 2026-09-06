namespace MacroDeck.Sdk.ConfigFlow;

/// <summary>
/// Runtime access to the config entries the user created for this integration through its
/// config flow. Secret values are stored encrypted and only resolved on demand via
/// <see cref="GetSecretAsync"/>.
/// </summary>
public interface IIntegrationConfig
{
	/// <summary>All config entries the user set up for this integration.</summary>
	Task<IReadOnlyList<ConfigEntrySnapshot>> GetEntriesAsync(CancellationToken cancellationToken = default);

	/// <summary>Reads a plain (non-secret) value of an entry, or <c>null</c> if absent.</summary>
	Task<string?> GetStringAsync(Guid entryId, string key, CancellationToken cancellationToken = default);

	/// <summary>Resolves a secret field of an entry to plaintext, or <c>null</c> if absent.</summary>
	Task<string?> GetSecretAsync(Guid entryId, string key, CancellationToken cancellationToken = default);

	/// <summary>
	/// Writes a plain (non-secret) value on an entry. Used by integrations that need to persist
	/// runtime state (e.g. refreshed metadata) back into their config entry.
	/// </summary>
	Task SetStringAsync(Guid entryId, string key, string? value, CancellationToken cancellationToken = default);

	/// <summary>
	/// Writes a secret value on an entry, stored encrypted. Used to persist rotating credentials
	/// such as refreshed OAuth access tokens.
	/// </summary>
	Task SetSecretAsync(Guid entryId, string key, string value, CancellationToken cancellationToken = default);
}
