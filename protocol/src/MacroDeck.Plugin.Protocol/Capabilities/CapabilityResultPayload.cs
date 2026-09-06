using System.Text.Json;

namespace MacroDeck.Plugin.Protocol.Capabilities;

/// <summary>
/// Payload of <c>capability.result</c>, correlated to the <c>capability.invoke</c> it answers.
///
/// <para>
/// Carries only the success value. A failed invocation sets the envelope's <c>error</c> instead, which
/// the envelope contract already makes mutually exclusive with the payload - so there is exactly one
/// place to look for a failure, and no representable state where a result is both.
/// </para>
/// </summary>
public sealed record CapabilityResultPayload
{
	/// <summary>The operation's return value, shaped by the kind and operation. Absent when it returns
	/// nothing.</summary>
	public JsonElement? Data { get; init; }
}
