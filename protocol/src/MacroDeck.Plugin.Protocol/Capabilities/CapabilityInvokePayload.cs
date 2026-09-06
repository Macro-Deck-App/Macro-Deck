using System.Text.Json;

namespace MacroDeck.Plugin.Protocol.Capabilities;

/// <summary>
/// Payload of <c>capability.invoke</c>. Names one declared capability and what to do with it.
///
/// <para>
/// Deliberately carries neither a deadline nor an idempotency key: both already live on
/// <c>ProtocolEnvelope</c>, and a second copy on the payload would be a second source of truth that
/// the two peers could disagree about. The same applies to the correlation id - the reply correlates
/// to the envelope's <c>id</c>.
/// </para>
/// </summary>
public sealed record CapabilityInvokePayload
{
	/// <summary>One of <c>CapabilityKinds</c>.</summary>
	public required string Kind { get; init; }

	/// <summary>The declared local id of the capability being invoked, unqualified.</summary>
	public required string LocalId { get; init; }

	/// <summary>What to do with it. The set of operations is defined per capability kind.</summary>
	public required string Operation { get; init; }

	/// <summary>Operation arguments, shaped by the kind and operation. Absent when there are none.</summary>
	public JsonElement? Arguments { get; init; }
}
