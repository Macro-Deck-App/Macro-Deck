using MacroDeck.Plugin.Protocol.Handshake;

namespace MacroDeck.Plugin.Protocol.Capabilities;

/// <summary>
/// Payload of <c>capability.declare</c>. The catalogue is complete, not a delta: a re-declaration
/// replaces what the session already knows rather than adding to it, so a plugin that loses a
/// capability has a way to say so.
///
/// <para>
/// The catalogue sent at <c>POST /api/plugins/sessions</c> is the authoritative one for a new session.
/// This message exists for the mid-session case, where something the plugin declares only becomes
/// known after the user configures it.
/// </para>
/// </summary>
public sealed record CapabilityDeclarePayload
{
	public required IReadOnlyList<DeclaredCapability> Capabilities { get; init; }
}
