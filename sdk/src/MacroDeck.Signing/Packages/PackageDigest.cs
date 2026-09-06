using System.Text.Json.Nodes;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeck.Signing.Packages;

/// <summary>Dispatches canonical digest computation to the format that owns it: <see cref="PluginArtifactDigest"/>
/// for a plugin (which needs the typed <see cref="PluginManifest"/> model), or <see cref="IconPackDigest"/>
/// / <see cref="PortablePackageDigest"/> directly from the manifest's <see cref="JsonNode"/> for the three
/// formats with no typed model in the SDK.</summary>
internal static class PackageDigest
{
	public static PackageDigestResult Compute(SignablePackageFormat format,
		JsonObject manifestNode,
		string manifestJson,
		IPluginManifestReader manifestReader)
	{
		if (format != SignablePackageFormat.Plugin)
		{
			return format == SignablePackageFormat.IconPack
				? IconPackDigest.Compute(manifestNode)
				: PortablePackageDigest.Compute(manifestNode);
		}

		if (!TryProbeIdAndVersion(manifestNode, out var id, out var version))
		{
			return PackageDigestResult.Fail(SigningError.ManifestMalformed,
				"The manifest has no non-empty string 'id' and 'version' properties.");
		}

		var readResult = manifestReader.ReadFromJson(manifestJson, versionDirectory: null, id, version);
		if (!readResult.Success)
		{
			return PackageDigestResult.Fail(SigningError.ManifestMalformed,
				readResult.ErrorMessage ?? "The manifest is invalid.");
		}

		return PackageDigestResult.Ok(PluginArtifactDigest.Compute(readResult.Manifest!));
	}

	private static bool TryProbeIdAndVersion(JsonObject manifest, out string id, out string version)
	{
		id = manifest["id"] is JsonValue idValue && idValue.TryGetValue<string>(out var idText) ? idText : string.Empty;
		version = manifest["version"] is JsonValue versionValue && versionValue.TryGetValue<string>(out var versionText)
			? versionText
			: string.Empty;

		return id.Length > 0 && version.Length > 0;
	}
}
