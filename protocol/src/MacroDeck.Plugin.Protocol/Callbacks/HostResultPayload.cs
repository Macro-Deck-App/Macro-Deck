using System.Text.Json;

namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>
/// Payload of <c>host.result</c>, correlated to the <c>host.invoke</c> it answers. Mirrors
/// <c>CapabilityResultPayload</c>: carries only the success value, since a failed invocation sets
/// the envelope's <c>error</c> instead.
/// </summary>
public sealed record HostResultPayload
{
	/// <summary>The operation's return value, shaped by the API and operation. Absent when it returns
	/// nothing.</summary>
	public JsonElement? Data { get; init; }
}
