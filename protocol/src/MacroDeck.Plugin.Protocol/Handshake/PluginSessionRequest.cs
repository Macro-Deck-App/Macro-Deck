using MacroDeck.Plugin.Protocol.Versioning;

namespace MacroDeck.Plugin.Protocol.Handshake;

/// <summary>
/// Body of <c>POST /api/plugins/sessions</c>, authenticated by the plugin id and secret headers.
/// Negotiation happens here, once, and is authoritative - the subsequent <c>session.hello</c> only
/// asserts the outcome.
/// </summary>
public sealed record PluginSessionRequest
{
	public required ProtocolVersionRange RequestedVersion { get; init; }

	public required IReadOnlyList<DeclaredCapability> Capabilities { get; init; }

	/// <summary>
	/// The plugin's own version, as declared by the <c>version</c> field of the plugin's <c>manifest.json</c>.
	/// Carried on the session rather than on registration: the session handshake runs on every connect,
	/// so a plugin that updates itself reports its new version without re-enrolling, whereas registration
	/// happens once. Optional and additive - protocol v1 requires every older plugin that sends nothing
	/// to still negotiate a session normally.
	/// </summary>
	public string? DeclaredVersion { get; init; }

	/// <summary>
	/// The plugin's own display name for this session, as declared by the plugin process itself.
	/// Names the session only - it never writes back to the enrollment record, so an enrolled plugin's
	/// registered display name stays exactly what it enrolled with. Optional and additive, for the same
	/// reason as <see cref="DeclaredVersion" />: an older plugin that sends nothing still negotiates
	/// normally.
	/// </summary>
	public string? DeclaredName { get; init; }

	/// <summary>
	/// What the plugin was built against, and which deprecated APIs it actually uses. Optional and
	/// additive for the same reason as <see cref="DeclaredVersion" />: a plugin that sends nothing must
	/// still negotiate normally, and the host reports its compatibility as unknown rather than guessing.
	/// </summary>
	public PluginSdkUsage? Sdk { get; init; }
}
