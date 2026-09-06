namespace MacroDeck.Plugin.Packaging.Artifacts;

/// <summary>
/// Names and extensions of the installable plugin artifact, alongside
/// <c>PortableFileExtensions</c> and <c>IconImportFiles.MacroDeckIconPackExtension</c>.
/// </summary>
public static class PluginArtifactFiles
{
	/// <summary>The installable plugin artifact: a ZIP whose root <em>is</em> the version directory.
	/// There is deliberately no generic <c>.macroDeckPackage</c> wrapper - a plugin and an icon pack are
	/// separate store artifacts.</summary>
	public const string MacroDeckPluginExtension = ".macroDeckPlugin";

	/// <summary>The manifest's fixed name at the artifact root, matching where the supervisor's reader
	/// already looks inside an installed version directory.</summary>
	public const string ManifestFileName = "manifest.json";

	/// <summary>The signing certificate a signed plugin carries at its archive root, exactly as issued -
	/// signature material, not part of the signed payload, so it is excluded from <c>files[]</c> and from
	/// the canonical digest.</summary>
	public const string CertificateFileName = "certificate.json";

	/// <summary>The root key's detached signature over <see cref="CertificateFileName"/>'s exact bytes,
	/// carried alongside it at the archive root. Signature material, excluded the same way.</summary>
	public const string CertificateSignatureFileName = "certificate.sig";

	/// <summary>Names the active version. Written by the installer, read by the installation catalog.
	/// </summary>
	public const string CurrentFileName = "current.json";

	/// <summary>Immutable per-version directories live under here.</summary>
	public const string VersionsDirectoryName = "versions";

	/// <summary>Plugin-owned state, a sibling of <see cref="VersionsDirectoryName"/> so it survives every
	/// update, rollback and (by default) uninstall.</summary>
	public const string DataDirectoryName = "data";

	/// <summary>Extraction happens here before activation. The leading underscore keeps it out of the
	/// installation catalog, which skips any directory failing the reverse-domain plugin id pattern.
	/// </summary>
	public const string StagingDirectoryName = "_staging";

	/// <summary>Optional retained-download cache. Underscore-prefixed for the same reason as
	/// <see cref="StagingDirectoryName"/>.</summary>
	public const string CacheDirectoryName = "_cache";

	/// <summary>True when <paramref name="path"/> carries the artifact extension, case-insensitively -
	/// the extension is camel-cased by convention but file systems disagree about case.</summary>
	public static bool HasArtifactExtension(string? path)
	{
		return !string.IsNullOrWhiteSpace(path) &&
			Path.GetExtension(path).Equals(MacroDeckPluginExtension, StringComparison.OrdinalIgnoreCase);
	}
}
