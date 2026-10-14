namespace MacroDeck.Sdk.Storage;

/// <summary>
/// Small text documents an integration keeps for itself across restarts, such as a list of devices it has
/// seen. Each document is addressed by a key that follows <see cref="IntegrationStorageKeys" />. Storage is
/// private to the integration and travels with the Integrations part of a Macro Deck backup.
/// <para>
/// A write is atomic: it publishes a temporary copy over the document and keeps the previous document as a
/// backup, so an interrupted write never leaves a document half written. A read returns the first copy the
/// integration's validator accepts, in the order document, pending write, backup.
/// </para>
/// </summary>
public interface IIntegrationStorage
{
	/// <summary>
	/// Reads the document stored under <paramref name="key" />. <paramref name="isValid" /> decides whether a
	/// copy is usable; without it every non-empty copy is. Reports
	/// <see cref="IntegrationStorageReadStatus.Missing" /> when nothing was ever stored and
	/// <see cref="IntegrationStorageReadStatus.Unreadable" /> when copies exist but none is valid. An
	/// integration that gets <see cref="IntegrationStorageReadStatus.Unreadable" /> should not write the key
	/// again in that session, or it replaces the copies a user could still recover.
	/// </summary>
	/// <exception cref="ArgumentException"><paramref name="key" /> is not a valid key.</exception>
	Task<IntegrationStorageReadResult> ReadAsync(
		string key,
		Func<string, bool>? isValid = null,
		CancellationToken cancellationToken = default);

	/// <summary>Stores <paramref name="content" /> under <paramref name="key" />, replacing the document.</summary>
	/// <exception cref="ArgumentException"><paramref name="key" /> is not a valid key, or
	/// <paramref name="content" /> is empty.</exception>
	/// <exception cref="InvalidOperationException">This context has nowhere to store documents.</exception>
	Task WriteAsync(string key, string content, CancellationToken cancellationToken = default);

	/// <summary>Deletes the document under <paramref name="key" /> and its copies. A missing key is not an error.</summary>
	/// <exception cref="ArgumentException"><paramref name="key" /> is not a valid key.</exception>
	Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
