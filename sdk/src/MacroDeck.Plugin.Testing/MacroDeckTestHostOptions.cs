using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;

namespace MacroDeck.Plugin.Testing;

/// <summary>
/// How <see cref="MacroDeckTestHost" /> presents itself: what it advertises, how it negotiates a
/// declared capability, and how it answers registration.
/// </summary>
public sealed class MacroDeckTestHostOptions
{
	/// <summary>
	/// The protocol versions the discovery endpoint and session response advertise as supported.
	/// Defaults to <see cref="ProtocolVersions.Supported" />.
	/// </summary>
	public IReadOnlyList<int> OfferedVersions { get; init; } = ProtocolVersions.Supported;

	/// <summary>
	/// Decides whether one declared capability is accepted, and at what version. Defaults to
	/// <see cref="CapabilityVersionNegotiator.Negotiate" /> - the same rule a real host applies -
	/// called once per capability the plugin declares. Override it to simulate a host that rejects a
	/// specific kind.
	/// </summary>
	public Func<DeclaredCapability, CapabilityNegotiationResult>? NegotiateCapability { get; init; }

	/// <summary>
	/// The limits advertised to a connecting plugin. Defaults to
	/// <see cref="ProtocolDescriptorFactory.CreateLimitsDescriptor" /> - the protocol's own real limits,
	/// so a plugin under test sees the same numbers it would against a real host.
	/// </summary>
	public PluginProtocolLimitsDescriptor Limits { get; init; } = ProtocolDescriptorFactory.CreateLimitsDescriptor();

	/// <summary>
	/// The timeouts advertised to a connecting plugin. Defaults to
	/// <see cref="ProtocolDescriptorFactory.CreateTimeoutsDescriptor" />.
	/// </summary>
	public PluginProtocolTimeoutsDescriptor Timeouts { get; init; }
		= ProtocolDescriptorFactory.CreateTimeoutsDescriptor();

	/// <summary>How <c>POST /api/plugins/registration</c> is answered. Defaults to <see cref="PluginRegistrationPolicy.AcceptAny" />.</summary>
	public PluginRegistrationPolicy Registration { get; init; } = PluginRegistrationPolicy.AcceptAny;

	/// <summary>
	/// Gates <c>POST /api/plugins/sessions</c>. Defaults to <see cref="PluginSessionGate.Open" />, so a
	/// session opens as soon as a plugin asks for one - unchanged from before this option existed. Set
	/// to <see cref="PluginSessionGate.Held" /> to hold every session request until released, which is
	/// what makes it possible to observe a plugin's <c>/_macrodeck/health</c> answering while no session
	/// can possibly exist yet, rather than merely usually not existing yet.
	/// </summary>
	public PluginSessionGate SessionCreation { get; init; } = PluginSessionGate.Open;

	/// <summary>
	/// How a pairing request created through <c>POST /api/plugins/pairing</c> is answered. Defaults to
	/// <see cref="PluginPairingPolicy.AutoApprove" />, so a conformance run - or any other automated
	/// harness - never blocks on a human clicking Approve in the desktop app.
	/// </summary>
	public PluginPairingPolicy Pairing { get; init; } = PluginPairingPolicy.AutoApprove;

	/// <summary>Resolves a negotiation result for <paramref name="capability" />, applying <see cref="NegotiateCapability" /> when set.</summary>
	internal CapabilityNegotiationResult Negotiate(DeclaredCapability capability)
		=> NegotiateCapability is { } negotiate
			? negotiate(capability)
			: CapabilityVersionNegotiator.Negotiate(capability.Kind, capability.VersionRange);
}
