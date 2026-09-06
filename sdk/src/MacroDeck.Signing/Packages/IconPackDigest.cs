using System.Text;
using System.Text.Json.Nodes;

namespace MacroDeck.Signing.Packages;

/// <summary>
/// The canonical byte sequence an icon pack (<c>.macroDeckIconPack</c>, manifest entry <c>pack.json</c>)
/// signature covers. Computed directly from the manifest's <see cref="JsonNode"/>, since icon packs have
/// no typed manifest model in the SDK.
/// </summary>
public static class IconPackDigest
{
	private const string DocumentHeader = "macro-deck-iconpack/1";

	/// <summary>
	/// Builds the digest document. The layout is a public contract - changing it invalidates every icon
	/// pack signature ever issued:
	/// <code>
	/// macro-deck-iconpack/1\n
	/// &lt;id&gt;\n
	/// &lt;name&gt;\n
	/// &lt;author&gt;\n                                     (empty when absent)
	/// &lt;version&gt;\n                                    (empty when absent)
	/// f\t&lt;path&gt;\t&lt;sha256&gt;\t&lt;size&gt;\n   (one per declared file, sorted ordinally by path)
	/// </code>
	/// </summary>
	public static PackageDigestResult Compute(JsonObject manifest)
	{
		ArgumentNullException.ThrowIfNull(manifest);

		if (manifest["id"] is not JsonValue idValue || !idValue.TryGetValue<string>(out var id) || id.Length == 0)
		{
			return PackageDigestResult.Fail(SigningError.ManifestMalformed,
				"The icon pack manifest has no non-empty 'id'.");
		}

		if (manifest["name"] is not JsonValue nameValue ||
			!nameValue.TryGetValue<string>(out var name) ||
			name.Length == 0)
		{
			return PackageDigestResult.Fail(SigningError.ManifestMalformed,
				"The icon pack manifest has no non-empty 'name'.");
		}

		var author = manifest["author"] is JsonValue authorValue && authorValue.TryGetValue<string>(out var authorText)
			? authorText
			: string.Empty;

		var version = manifest["version"] is JsonValue versionValue &&
			versionValue.TryGetValue<string>(out var versionText)
				? versionText
				: string.Empty;

		if (!DeclaredPackageFiles.TryParse(manifest, out var files, out var filesError))
		{
			return PackageDigestResult.Fail(SigningError.ManifestMalformed, filesError!);
		}

		var builder = new StringBuilder();
		builder.Append(DocumentHeader).Append('\n');
		builder.Append(id).Append('\n');
		builder.Append(name).Append('\n');
		builder.Append(author).Append('\n');
		builder.Append(version).Append('\n');

		foreach (var file in files.OrderBy(entry => entry.Path, StringComparer.Ordinal))
		{
			builder.Append("f\t").Append(file.Path).Append('\t')
				.Append(file.Sha256).Append('\t')
				.Append(file.Size).Append('\n');
		}

		return PackageDigestResult.Ok(Encoding.UTF8.GetBytes(builder.ToString()));
	}
}
