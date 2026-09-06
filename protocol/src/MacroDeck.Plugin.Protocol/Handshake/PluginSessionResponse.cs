using MacroDeck.Plugin.Protocol.Compatibility;
using MacroDeck.Plugin.Protocol.Versioning;

namespace MacroDeck.Plugin.Protocol.Handshake;

/// <summary>Response of <c>POST /api/plugins/sessions</c>: the negotiated version and capability map,
/// the session token to present on the WebSocket upgrade, and the limits and timeouts in force.</summary>
public sealed record PluginSessionResponse
{
	public required string SessionId { get; init; }

	public required string SessionToken { get; init; }

	public required int NegotiatedVersion { get; init; }

	public required IReadOnlyList<CapabilityNegotiationResult> Capabilities { get; init; }

	public required PluginProtocolLimitsDescriptor Limits { get; init; }

	public required PluginProtocolTimeoutsDescriptor Timeouts { get; init; }

	/// <summary>
	/// The host's compatibility verdict on this plugin. Nullable rather than required so a plugin built
	/// against a newer SDK can still deserialize a response from an older host that does not send it.
	/// </summary>
	public PluginCompatibilityReport? Compatibility { get; init; }
}
