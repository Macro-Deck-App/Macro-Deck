using System.Text.Json;

namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>
/// Payload of <c>host.invoke</c>. Names one host API and what to do with it - the reverse of
/// <c>CapabilityInvokePayload</c>. Deliberately carries neither a deadline nor an idempotency key,
/// for the same reason as its capability-direction counterpart: both already live on
/// <c>ProtocolEnvelope</c>.
/// </summary>
public sealed record HostInvokePayload
{
	/// <summary>One of <see cref="HostApis" />.</summary>
	public required string Api { get; init; }

	/// <summary>What to do with it. The set of operations is defined per API by <see cref="HostOperations" />.</summary>
	public required string Operation { get; init; }

	/// <summary>Operation arguments, shaped by the API and operation. Absent when there are none.</summary>
	public JsonElement? Arguments { get; init; }
}
