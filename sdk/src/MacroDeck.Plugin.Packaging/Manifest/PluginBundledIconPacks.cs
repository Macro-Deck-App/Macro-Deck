using System.Text.RegularExpressions;

namespace MacroDeck.Plugin.Packaging.Manifest;

/// <summary>The shape rules for <see cref="PluginManifest.BundledIconPacks"/>, shared by the manifest
/// reader, the Plugin CLI and the host.</summary>
public static partial class PluginBundledIconPacks
{
	public const int MaxKeyLength = 64;

	public const int MaxCount = 32;

	/// <summary>The extension every bundled pack path ends with, compared case-insensitively.</summary>
	public const string FileExtension = ".macroDeckIconPack";

	/// <summary>The project folder the Plugin CLI places bundled packs in.</summary>
	public const string DefaultDirectory = "icon-packs";

	public static bool IsValidKey(string? key) => key is { Length: > 0 and <= MaxKeyLength } && KeyPattern().IsMatch(key);

	/// <summary>Turns a pack name into a key: lowercase ASCII letters and digits, runs of anything else
	/// collapsed into one hyphen. Null when nothing usable is left.</summary>
	public static string? SlugFromName(string? name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return null;
		}

		var slug = NonKeyCharacters().Replace(name.Trim().ToLowerInvariant(), "-").Trim('-');
		if (slug.Length > MaxKeyLength)
		{
			slug = slug[..MaxKeyLength].TrimEnd('-');
		}

		return IsValidKey(slug) ? slug : null;
	}

	public static string DefaultPath(string key) => $"{DefaultDirectory}/{key}{FileExtension}";

	[GeneratedRegex(@"\A[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\z", RegexOptions.CultureInvariant)]
	private static partial Regex KeyPattern();

	[GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
	private static partial Regex NonKeyCharacters();
}
