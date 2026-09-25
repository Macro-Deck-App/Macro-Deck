namespace MacroDeck.Plugin.Protocol.Assets;

/// <summary>The kinds of binary asset a plugin can upload through <c>asset.*</c>.</summary>
public static class AssetKinds
{
	public const string Icon = "icon";

	public const string Artwork = "artwork";

	/// <summary>
	/// The image an <c>IIconProviderActionDefinition</c> instance supplies for a widget. Deliberately not
	/// <see cref="Icon" />: that kind is what <c>RemotePluginIntegrationRegistrar</c> watches to learn a
	/// plugin's <em>own</em> catalog icon changed, and a provider's per-widget image - which can change
	/// on every poll - has nothing to do with the plugin's own icon.
	/// </summary>
	public const string ActionIcon = "action-icon";

	/// <summary>
	/// Bytes a plugin registers as a UI resource through <c>ui</c>/<c>register-resource</c>. Limited to
	/// <see cref="UiResourceRules.SupportedMediaTypes" /> and
	/// <see cref="Limits.ProtocolLimits.MaxUiResourceBytes" />, and held in memory only: the host never
	/// writes them to its on-disk asset cache.
	/// </summary>
	public const string UiResource = "ui-resource";

	/// <summary>
	/// A <c>.macroDeckIconPack</c> archive a development session uploads for <c>icon-packs</c>/<c>sync-bundled</c>.
	/// Held in memory only and bounded by <see cref="Limits.ProtocolLimits.MaxAssetBytes" />.
	/// </summary>
	public const string IconPack = "icon-pack";

	public static readonly IReadOnlyList<string> All = [Icon, Artwork, ActionIcon, UiResource, IconPack];

	private static readonly HashSet<string> _known = new(All, StringComparer.Ordinal);

	public static bool IsKnown(string? kind) => kind is not null && _known.Contains(kind);
}
