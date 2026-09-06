using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace MacroDeck.Signing.Packages;

/// <summary>
/// The canonical byte sequence a portable package (<c>.macroDeckProfile</c>, <c>.macroDeckFolder</c> or
/// <c>.macroDeckWidget</c>, manifest entry <c>manifest.json</c>) signature covers. Computed directly from
/// the manifest's <see cref="JsonNode"/>, since these formats have no typed manifest model in the SDK.
/// </summary>
public static class PortablePackageDigest
{
	private const string DocumentHeader = "macro-deck-portable/1";

	/// <summary>
	/// Builds the digest document. The layout is a public contract - changing it invalidates every
	/// portable package signature ever issued:
	/// <code>
	/// macro-deck-portable/1\n
	/// &lt;kind&gt;\n              (the manifest's kind, lower-cased: profile, folder or widgets)
	/// &lt;formatVersion&gt;\n
	/// &lt;appVersion&gt;\n        (empty when absent)
	/// &lt;includesSecrets&gt;\n   (true or false, lowercase invariant; false when absent)
	/// f\t&lt;path&gt;\t&lt;sha256&gt;\t&lt;size&gt;\n   (one per declared file, sorted ordinally by path)
	/// </code>
	/// </summary>
	public static PackageDigestResult Compute(JsonObject manifest)
	{
		ArgumentNullException.ThrowIfNull(manifest);

		if (manifest["kind"] is not JsonValue kindValue ||
			!kindValue.TryGetValue<string>(out var kind) ||
			kind.Length == 0)
		{
			return PackageDigestResult.Fail(SigningError.ManifestMalformed, "The manifest has no non-empty 'kind'.");
		}

		string formatVersion;
		if (manifest["formatVersion"] is JsonValue formatVersionValue)
		{
			formatVersion = formatVersionValue.TryGetValue<string>(out var text)
				? text
				: formatVersionValue.TryGetValue<long>(out var number)
					? number.ToString(CultureInfo.InvariantCulture)
					: string.Empty;
		}
		else
		{
			formatVersion = string.Empty;
		}

		if (formatVersion.Length == 0)
		{
			return PackageDigestResult.Fail(SigningError.ManifestMalformed,
				"The manifest has no non-empty 'formatVersion'.");
		}

		var appVersion = manifest["appVersion"] is JsonValue appVersionValue &&
			appVersionValue.TryGetValue<string>(out var appVersionText)
				? appVersionText
				: string.Empty;

		var includesSecrets = manifest["includesSecrets"] is JsonValue includesSecretsValue &&
			includesSecretsValue.TryGetValue<bool>(out var includesSecretsFlag) &&
			includesSecretsFlag;

		if (!DeclaredPackageFiles.TryParse(manifest, out var files, out var filesError))
		{
			return PackageDigestResult.Fail(SigningError.ManifestMalformed, filesError!);
		}

		var builder = new StringBuilder();
		builder.Append(DocumentHeader).Append('\n');
		builder.Append(kind.ToLowerInvariant()).Append('\n');
		builder.Append(formatVersion).Append('\n');
		builder.Append(appVersion).Append('\n');
		builder.Append(includesSecrets ? "true" : "false").Append('\n');

		foreach (var file in files.OrderBy(entry => entry.Path, StringComparer.Ordinal))
		{
			builder.Append("f\t").Append(file.Path).Append('\t')
				.Append(file.Sha256).Append('\t')
				.Append(file.Size).Append('\n');
		}

		return PackageDigestResult.Ok(Encoding.UTF8.GetBytes(builder.ToString()));
	}
}
