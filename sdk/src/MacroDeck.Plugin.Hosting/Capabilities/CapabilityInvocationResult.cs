using System.Text.Json;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Hosting.Capabilities;

/// <summary>
/// What an invocation produced. Either data or an error, never both - the envelope makes the two
/// mutually exclusive on the wire, and this mirrors that so there is no representable result the
/// transport would have to reject.
/// </summary>
public sealed record CapabilityInvocationResult
{
	private CapabilityInvocationResult()
	{
	}

	/// <summary>The success value, when there is one.</summary>
	public JsonElement? Data { get; private init; }

	/// <summary>The failure, when the invocation failed.</summary>
	public ProtocolError? Error { get; private init; }

	/// <summary>Whether this result is a failure.</summary>
	public bool IsFailure => Error is not null;

	/// <summary>Succeeded, with nothing to return.</summary>
	public static CapabilityInvocationResult Ok() => new();

	/// <summary>Succeeded, returning <paramref name="value" /> serialized with the protocol's options.</summary>
	public static CapabilityInvocationResult Ok<T>(T value)
		=> new() { Data = JsonSerializer.SerializeToElement(value, PluginProtocolJson.Options) };

	/// <summary>Succeeded, returning an already-serialized value.</summary>
	public static CapabilityInvocationResult Ok(JsonElement data) => new() { Data = data };

	/// <summary>
	/// Failed, for a reason the caller can act on.
	/// </summary>
	/// <param name="code">One of <see cref="ProtocolErrorCodes" />.</param>
	/// <param name="message">
	/// Shown to the user, so it must read as an explanation and must not carry tokens, paths or
	/// provider internals. It is truncated to the protocol's limit before it is sent.
	/// </param>
	/// <param name="retryable">Whether repeating the same invocation could succeed.</param>
	public static CapabilityInvocationResult Failed(string code, string message, bool retryable = false)
		=> new()
		{
			Error = new ProtocolError
			{
				Code = code,
				Message = message,
				Retryable = retryable
			}
		};

	/// <summary>Failed, with an error already shaped by the protocol.</summary>
	public static CapabilityInvocationResult Failed(ProtocolError error)
		=> new() { Error = error };
}
