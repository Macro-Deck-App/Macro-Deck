using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Migration;

/// <summary>
/// Reads one foreign application's on-disk format and translates it into <see cref="MigrationPlan" />.
/// A source only translates: id generation, reference rewriting and persistence stay with the importer
/// the plan is handed to.
/// </summary>
public interface IMigrationSource
{
	string Id { get; }

	/// <summary>Display name of the application this source reads, e.g. "Macro Deck 2".</summary>
	string Name { get; }

	/// <summary>
	/// Where this application keeps its data on this machine, when that can be known without asking.
	/// Null on a platform the application never ran on, which is not an error - the user picks a folder.
	/// </summary>
	string? TryDetectDefaultPath();

	/// <summary>Whether <paramref name="path" /> looks like this application's data directory.</summary>
	bool Recognizes(string path);

	/// <summary>
	/// Reads the installation at <see cref="MigrationRequest.Path" />. Writes nothing, so the caller may
	/// run it to build a preview and again to apply.
	/// </summary>
	Task<Result<MigrationPlan, MigrationError>> Read(MigrationRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// <paramref name="DecryptionKey" /> overrides the key the source would read itself;
/// <paramref name="SkipDecryption" /> abandons encrypted data entirely and migrates the rest. Supplying
/// neither means "read the key the way the source application does".
/// </summary>
public sealed record MigrationRequest(string SourceId, string Path, string? DecryptionKey, bool SkipDecryption);
