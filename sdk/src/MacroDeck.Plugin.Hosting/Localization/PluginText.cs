using MacroDeck.Localization;
using MacroDeck.Plugin.Protocol.Versioning;

namespace MacroDeck.Plugin.Hosting.Localization;

/// <summary>
/// Decides what a <see cref="LocalizedText" /> descriptor field carries on the wire.
/// </summary>
/// <remarks>
/// <para>
/// From <see cref="ProtocolVersions.LocalizedDescriptors" /> onwards a reference travels as a
/// reference, and the client that renders it resolves it in its reader's language against the catalog
/// the plugin registered. Below that version the descriptor DTOs were plain strings, so a reference is
/// resolved here instead - against the plugin's own catalog in its own default language, because
/// putting <c>[[plugin:x:Key]]</c> in front of a user is worse than putting the author's language
/// there. The negotiated version is what decides, not the plugin's own: an older host would reject the
/// object shape outright.
/// </para>
/// <para>
/// A plugin's text inside a Macro Deck UI tree never went through here - that travels as
/// <c>{"$localized":…}</c> and always has. This is only about the descriptor metadata.
/// </para>
/// <para>
/// Process-wide state because there is exactly one plugin, and therefore exactly one catalog and one
/// host session, per plugin process; the mappers that need it are static and reached from the transport
/// rather than from a container.
/// </para>
/// </remarks>
internal static class PluginText
{
	private static LocalizationResolver? _resolver;
	private static string? _culture;
	private static int _negotiatedVersion = ProtocolVersions.Minimum;

	/// <summary>Adopts the catalog the plugin registered, so references resolve in its own language.</summary>
	internal static void Use(ILocalizationCatalog catalog)
	{
		var registry = new LocalizationCatalogRegistry();
		registry.Register(catalog);

		_resolver = new LocalizationResolver(registry);
		_culture = catalog.DefaultCulture;
	}

	/// <summary>Records what the handshake settled on, deciding whether references may cross the wire.</summary>
	internal static void Negotiated(int protocolVersion) => _negotiatedVersion = protocolVersion;

	/// <summary>The text a descriptor field carries. Empty for an unset property.</summary>
	public static LocalizedText ToWire(LocalizedText text)
	{
		if (!text.IsLocalized || _negotiatedVersion >= ProtocolVersions.LocalizedDescriptors)
		{
			return text;
		}

		return LocalizedText.FromLiteral(_resolver?.Resolve(text, _culture) ?? string.Empty);
	}

	/// <summary>The text a nullable descriptor field carries, keeping "unset" distinct from "empty".</summary>
	/// <remarks>
	/// The cast is load-bearing. Without it the conditional's best common type is <c>LocalizedText</c>,
	/// not <c>LocalizedText?</c> - the null literal converts through the implicit <c>string</c> operator -
	/// so the "unset" branch yields an empty value wrapped in a non-null nullable, and every optional
	/// descriptor field ships as present-but-blank instead of absent.
	/// </remarks>
	public static LocalizedText? ToWireOrNull(LocalizedText text) => text.IsEmpty ? (LocalizedText?)null : ToWire(text);

	/// <summary>
	/// Flattens unconditionally, for the fields the protocol still types as a plain string: the
	/// handshake's capability display name, which is declared before a version is negotiated, and the
	/// error envelope, which is shared with transport-level failures rather than owned by this plugin.
	/// </summary>
	public static string ToLiteral(LocalizedText text)
		=> text.IsLocalized ? _resolver?.Resolve(text, _culture) ?? string.Empty : text.Literal ?? string.Empty;
}
