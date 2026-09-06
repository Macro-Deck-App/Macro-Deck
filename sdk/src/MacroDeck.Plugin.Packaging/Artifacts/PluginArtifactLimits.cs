namespace MacroDeck.Plugin.Packaging.Artifacts;

/// <summary>
/// Structural bounds on a <c>.macroDeckPlugin</c> artifact. Every byte count here is checked while
/// copying through a bounded stream, never taken from the archive's own declared entry sizes - a hostile
/// archive lies about those, which is the lesson the portable-archive reader already records.
/// </summary>
public static class PluginArtifactLimits
{
	/// <summary>A self-contained .NET plugin for three RIDs runs to a few thousand files.</summary>
	public const int MaxEntries = 20_000;

	/// <summary>Sized from <see cref="MaxEntries"/>: the packer recomputes <c>files[]</c> with a path, a
	/// digest and a size per entry, so a manifest naming every permitted entry has to fit here - entry
	/// count, not manifest size, is the gate on how many files an artifact may carry. Every manifest
	/// reader in the product applies this same bound, so an artifact that packs can always be read
	/// back.</summary>
	public const int MaxManifestBytes = 8 * 1024 * 1024;

	/// <summary>One extracted file. Large enough for a single self-contained binary.</summary>
	public const long MaxEntryBytes = 512L * 1024 * 1024;

	/// <summary>Everything the artifact expands to.</summary>
	public const long MaxTotalUncompressedBytes = 1024L * 1024 * 1024;

	/// <summary>The compressed artifact itself.</summary>
	public const long MaxArchiveBytes = 512L * 1024 * 1024;

	/// <summary>Total uncompressed over total compressed. Evaluated after extraction, so a zip bomb is
	/// caught by the byte caps first and by this ratio second.</summary>
	public const int MaxCompressionRatio = 100;

	/// <summary>Per entry, so <c>versions/&lt;version&gt;/&lt;entry&gt;</c> stays under Windows MAX_PATH in
	/// a default install location.</summary>
	public const int MaxPathLength = 200;

	/// <summary>Path segments per entry.</summary>
	public const int MaxPathDepth = 32;
}
