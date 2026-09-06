// Explicit, not implicit usings: this file is also linked into MacroDeck.Plugin.Analyzers, whose
// netstandard2.0 target does not carry the same implicit using set.

namespace MacroDeck.Plugin.Analyzers;

/// <summary>
/// The icon file extensions a manifest may declare, and the media type each one travels as.
///
/// <para>
/// One table, three consumers that must agree or an icon renders in one place and not another: the SDK
/// reads the manifest's icon at build time and reports this media type through <c>icons/describe</c>,
/// the host reads the same file out of an artifact for the pre-install confirmation card, and
/// <c>MacroDeck.Plugin.Analyzers</c> rejects an unsupported extension at compile time. The analyzer
/// cannot reference this assembly - Roslyn loads it into the compiler process on <c>netstandard2.0</c> -
/// so it links this file as source instead, exactly as it does with
/// <c>MacroDeck.Sdk.Identity.MacroDeckIdPatterns</c>. Keep everything here compiling on that target:
/// no <c>FrozenDictionary</c>, no newer BCL surface.
/// </para>
/// </summary>
public static class IconMediaTypes
{
	/// <summary>
	/// The extensions <see cref="ForExtension" /> answers for, lower-case and dot-prefixed. Exposed so a
	/// diagnostic or a validation message can name them without restating the list.
	/// </summary>
	// Wrapped, not a bare array: the static field is read-only but a bare string[] behind an
	// IReadOnlyList is still writable by anyone who casts it back, and this is public API on a package
	// a plugin loads into its own process.
	public static readonly IReadOnlyList<string> SupportedExtensions =
		Array.AsReadOnly(new[] { ".svg", ".png", ".jpg", ".jpeg", ".webp" });

	/// <summary>
	/// The media type <paramref name="iconPath" />'s extension implies, or <c>null</c> when the
	/// extension is not one this project renders. Matching is case-insensitive: a manifest may name
	/// <c>Assets/Logo.PNG</c> on a case-insensitive filesystem and still be the same icon.
	/// </summary>
	/// <param name="iconPath">A file name or relative path. A null, empty or extension-less value answers <c>null</c>.</param>
	public static string? ForExtension(string? iconPath)
	{
		if (string.IsNullOrWhiteSpace(iconPath))
		{
			return null;
		}

		return Path.GetExtension(iconPath).ToLowerInvariant() switch
		{
			".svg" => "image/svg+xml",
			".png" => "image/png",
			".jpg" or ".jpeg" => "image/jpeg",
			".webp" => "image/webp",
			_ => null
		};
	}
}
