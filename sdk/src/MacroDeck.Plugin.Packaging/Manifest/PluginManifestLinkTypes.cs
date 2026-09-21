namespace MacroDeck.Plugin.Packaging.Manifest;

/// <summary>
/// The <see cref="PluginManifestLink.Type"/> vocabulary. A standard type is labelled by Macro Deck in the
/// viewer's language and must not carry a <see cref="PluginManifestLink.Label"/>; <see cref="Custom"/>
/// must. <c>homepage</c> and <c>repository</c> are deliberately absent: they have their own manifest
/// fields. A type outside this list is a warning, never a rejection, so a type added later still
/// installs on an older host, which simply does not show it.
/// </summary>
public static class PluginManifestLinkTypes
{
	public const string Documentation = "documentation";

	public const string Wiki = "wiki";

	public const string Issues = "issues";

	public const string Support = "support";

	public const string Community = "community";

	public const string Donate = "donate";

	public const string Privacy = "privacy";

	public const string Terms = "terms";

	public const string Changelog = "changelog";

	public const string License = "license";

	public const string Custom = "custom";

	/// <summary>Every type Macro Deck labels itself. Excludes <see cref="Custom"/>.</summary>
	public static readonly IReadOnlyList<string> Standard =
	[
		Documentation, Wiki, Issues, Support, Community, Donate, Privacy, Terms, Changelog, License
	];

	private static readonly HashSet<string> _standard = new(Standard, StringComparer.Ordinal);

	/// <summary>Ordinal: link types are lowercase on the wire and <c>"Wiki"</c> is not <see cref="Wiki"/>.</summary>
	public static bool IsStandard(string? type)
	{
		return type is not null && _standard.Contains(type);
	}
}
