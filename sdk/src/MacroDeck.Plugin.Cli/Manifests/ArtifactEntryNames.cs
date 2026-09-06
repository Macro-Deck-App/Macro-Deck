using System.IO.Compression;

namespace MacroDeck.Plugin.Cli.Manifests;

/// <summary>Reads the entry names out of a <c>.macroDeckPlugin</c> artifact's ZIP central directory, for
/// <c>inspect --artifact</c>'s entrypoint-presence check.</summary>
internal static class ArtifactEntryNames
{
	/// <summary>
	/// Opens <paramref name="artifactPath" /> directly through <see cref="ZipArchive" /> rather than going
	/// through <c>PluginArtifactEntryPolicy</c> / <c>PluginArtifactLimits</c>. That is safe here only
	/// because this always runs <em>after</em> <c>IPluginArtifactReader.Inspect</c> has already opened,
	/// limit-checked and accepted the same archive - this second open is read-only reporting over content
	/// already judged safe, not a second acceptance decision. Do not reuse this helper anywhere that has
	/// not already gone through the artifact reader first.
	/// </summary>
	/// <returns>The archive's entry names, normalized with <see cref="EntrypointPresence.Normalize" /> and
	/// compared <see cref="StringComparer.OrdinalIgnoreCase" />. An empty set on any read failure - callers
	/// must treat an empty set as "could not check", never as "nothing present", so a corrupt read never
	/// produces a false "missing" warning.</returns>
	public static async Task<IReadOnlySet<string>> ReadAsync(string artifactPath, CancellationToken cancellationToken)
	{
		var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		try
		{
			await using var stream = new FileStream(artifactPath, FileMode.Open, FileAccess.Read, FileShare.Read);
			using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

			foreach (var entry in archive.Entries)
			{
				cancellationToken.ThrowIfCancellationRequested();
				names.Add(EntrypointPresence.Normalize(entry.FullName));
			}

			return names;
		}
		catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
		{
			return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		}
	}
}
