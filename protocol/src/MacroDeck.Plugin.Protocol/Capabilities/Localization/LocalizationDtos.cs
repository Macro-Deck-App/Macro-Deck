namespace MacroDeck.Plugin.Protocol.Capabilities.Localization;

/// <summary>
/// Result of the <c>describe</c> operation: which cultures this plugin ships resources for, and which
/// of them its own default-language resources are written in. Only the culture list travels here - the
/// strings themselves come one culture at a time through <c>catalog</c>, because a catalog is bounded
/// per culture but not across them.
/// </summary>
public sealed record LocalizationDescribeResult
{
	/// <summary>The scope the plugin owns, always <c>plugin:&lt;plugin-id&gt;</c>. Declared rather than
	/// assumed so the host can reject a plugin claiming a scope that is not its own.</summary>
	public required string Scope { get; init; }

	/// <summary>The culture the plugin's default-language resources are written in.</summary>
	public required string DefaultCulture { get; init; }

	/// <summary>Every culture the plugin can serve, <see cref="DefaultCulture" /> included.</summary>
	public required IReadOnlyList<string> Cultures { get; init; }
}

/// <summary>Arguments for the <c>catalog</c> operation.</summary>
public sealed record LocalizationCatalogArguments
{
	/// <summary>The culture to hand over. The host asks for the cultures its fallback chain needs, not
	/// for all of them.</summary>
	public required string Culture { get; init; }
}

/// <summary>Result of the <c>catalog</c> operation: one culture's strings.</summary>
public sealed record LocalizationCatalogResult
{
	/// <summary>The culture these entries are for, echoed back so a late reply cannot be mistaken for a
	/// different culture's.</summary>
	public required string Culture { get; init; }

	/// <summary>Key to template, for example <c>Configuration.Title</c> to <c>Connected as {userName}</c>.
	/// A plugin with nothing for this culture returns an empty map rather than failing; the host's
	/// fallback chain handles the rest.</summary>
	public required IReadOnlyDictionary<string, string> Entries { get; init; }
}
