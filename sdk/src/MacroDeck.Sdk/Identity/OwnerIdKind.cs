namespace MacroDeck.Sdk.Identity;

/// <summary>
/// Which rule an owner id has to satisfy. The two exist because two different populations of owner
/// share one namespace: integrations and plugins are packages, while a handful of host-internal
/// providers predate the package rule and are persisted in user data under their bare slug.
/// </summary>
public enum OwnerIdKind
{
	/// <summary>
	/// Reverse-domain, two or more segments (<c>app.macro-deck.spotify</c>,
	/// <c>com.suchbyte.test-plugin</c>). Every integration and every future plugin uses this. The
	/// multi-segment requirement is load-bearing: portable-archive export detects a profile's
	/// integration dependencies with a plain substring test, which is only specific enough because an
	/// owner id can never be a common word.
	/// </summary>
	Package,

	/// <summary>
	/// A single kebab segment (<c>macro-deck</c>, <c>music-player</c>, <c>time</c>). Reserved for the
	/// host's own event providers, which are not integrations and whose ids are already persisted
	/// inside stored widget triggers.
	/// </summary>
	HostProvider
}
