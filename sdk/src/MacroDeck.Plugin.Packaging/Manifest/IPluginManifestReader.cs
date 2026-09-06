namespace MacroDeck.Plugin.Packaging.Manifest;

/// <summary>Why a manifest read failed. <see cref="EntrypointMissing"/> means an entrypoint matched the
/// current RID's candidates but the resolved file does not exist on disk.</summary>
public enum PluginManifestError
{
	NotFound,
	Malformed,
	UnsupportedManifestVersion,
	InvalidPluginId,
	IdMismatch,
	VersionMismatch,
	NoEntrypoints,
	EntrypointOutsideVersionDirectory,
	EntrypointMissing,
	InvalidSettings,
	InvalidCompatibility,
	InvalidDependency,
	InvalidEntrypointRuntime,
	InvalidSignature,
	InvalidFileDigest,
	InvalidPermission,
	InvalidPublisher,

	/// <summary>The manifest's <see cref="PluginManifest.Icon"/> is not a safe relative forward-slash
	/// path. Existence on disk is never checked - see the reader for why.</summary>
	InvalidIcon,

	/// <summary>The manifest's <see cref="PluginManifest.Name"/> is blank, contains a control character,
	/// or exceeds the length bound. Appended at the end so a plugin compiled against an older SDK keeps
	/// its existing ordinal values.</summary>
	InvalidName,

	/// <summary>The manifest's <see cref="PluginManifest.Languages"/> carries a blank or duplicated tag.
	/// An unrecognised language tag is never an error - see the reader for why. Appended at the end for
	/// the same ordinal-stability reason as <see cref="InvalidName"/>.</summary>
	InvalidLanguage
}

/// <summary>Either a validated <see cref="Manifest"/>, or the <see cref="Error"/> that stopped it from
/// becoming one.</summary>
public sealed record PluginManifestReadResult
{
	public required bool Success { get; init; }

	public PluginManifest? Manifest { get; init; }

	public PluginManifestError? Error { get; init; }

	public string? ErrorMessage { get; init; }

	public static PluginManifestReadResult Ok(PluginManifest manifest) => new() { Success = true, Manifest = manifest };

	public static PluginManifestReadResult Fail(PluginManifestError error, string message)
		=> new() { Success = false, Error = error, ErrorMessage = message };
}

/// <summary>
/// Reads and validates a single <c>manifest.json</c>. Implemented in Infrastructure (file I/O); this
/// interface is what the supervisor and installation catalog depend on so they stay testable without
/// touching disk.
/// </summary>
public interface IPluginManifestReader
{
	/// <param name="manifestPath">Path to <c>manifest.json</c>.</param>
	/// <param name="expectedPluginId">Must equal the manifest's <see cref="PluginManifest.Id"/> - and
	/// the owning directory name, which the caller is responsible for passing here.</param>
	/// <param name="expectedVersion">Must equal the manifest's <see cref="PluginManifest.Version"/> - and
	/// the version directory name.</param>
	PluginManifestReadResult Read(string manifestPath, string expectedPluginId, string expectedVersion);

	/// <summary>
	/// Validates an already-read manifest document without touching disk. Used both by <see cref="Read"/>
	/// (which reads the file, then delegates here) and by the installer, which validates a manifest
	/// extracted from a <c>.macroDeckPlugin</c> artifact before it has a version directory to place it in.
	/// </summary>
	/// <param name="json">The manifest document text.</param>
	/// <param name="versionDirectory">The directory the manifest will live in, or <c>null</c> when none
	/// exists yet. When <c>null</c>, entrypoint path safety is still enforced, but resolving an
	/// entrypoint against a real root and checking that its file exists are both skipped.</param>
	/// <param name="expectedPluginId">Must equal the manifest's <see cref="PluginManifest.Id"/>.</param>
	/// <param name="expectedVersion">Must equal the manifest's <see cref="PluginManifest.Version"/>.</param>
	PluginManifestReadResult ReadFromJson(string json,
		string? versionDirectory,
		string expectedPluginId,
		string expectedVersion);
}
