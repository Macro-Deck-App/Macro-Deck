using System.Text.Json.Nodes;

namespace MacroDeck.Signing.Packages;

/// <summary>One entry of a manifest's <c>files</c> array, common to all four signable formats.</summary>
internal readonly record struct DeclaredPackageFile(string Path, string Sha256, long Size);

/// <summary>
/// Parses a manifest's <c>files</c> array directly from its <see cref="JsonNode"/> - never through a typed
/// model, since three of the four signable formats have none in the SDK. Used both for canonical digest
/// computation and for checking declared files against the archive.
/// </summary>
internal static class DeclaredPackageFiles
{
	public static bool TryParse(JsonObject manifest, out IReadOnlyList<DeclaredPackageFile> files, out string? error)
	{
		if (manifest["files"] is not JsonArray filesArray || filesArray.Count == 0)
		{
			files = [];
			error = "The manifest declares no files.";
			return false;
		}

		var parsed = new List<DeclaredPackageFile>(filesArray.Count);
		foreach (var node in filesArray)
		{
			if (node is not JsonObject entry)
			{
				files = [];
				error = "Every 'files' entry must be an object.";
				return false;
			}

			if (!TryGetNonEmptyString(entry, "path", out var path))
			{
				files = [];
				error = "A 'files' entry has no non-empty 'path'.";
				return false;
			}

			if (!TryGetNonEmptyString(entry, "sha256", out var sha256))
			{
				files = [];
				error = $"'{path}' has no non-empty 'sha256'.";
				return false;
			}

			if (entry["size"] is not JsonValue sizeValue || !sizeValue.TryGetValue<long>(out var size) || size < 0)
			{
				files = [];
				error = $"'{path}' has no non-negative 'size'.";
				return false;
			}

			parsed.Add(new DeclaredPackageFile(path, sha256, size));
		}

		files = parsed;
		error = null;
		return true;
	}

	/// <summary>Strips an optional <c>sha256:</c> prefix, so a declared digest compares equal to a freshly
	/// computed one regardless of which convention the manifest used.</summary>
	public static string StripSha256Prefix(string value) =>
		value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? value[7..] : value;

	private static bool TryGetNonEmptyString(JsonObject entry, string property, out string value)
	{
		if (entry[property] is JsonValue node && node.TryGetValue<string>(out var text) && text.Length > 0)
		{
			value = text;
			return true;
		}

		value = string.Empty;
		return false;
	}
}
