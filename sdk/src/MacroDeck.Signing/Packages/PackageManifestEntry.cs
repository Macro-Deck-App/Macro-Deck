using MacroDeck.Plugin.Packaging.Artifacts;

namespace MacroDeck.Signing.Packages;

/// <summary>Which archive entry carries a <see cref="SignablePackageFormat"/>'s manifest.</summary>
internal static class PackageManifestEntry
{
	/// <summary>The icon pack format's manifest is <c>pack.json</c>, not <c>manifest.json</c> - the only
	/// one of the four formats that differs.</summary>
	public const string IconPackManifestFileName = "pack.json";

	public static string NameFor(SignablePackageFormat format) => format switch
	{
		SignablePackageFormat.IconPack => IconPackManifestFileName,
		_ => PluginArtifactFiles.ManifestFileName
	};
}
