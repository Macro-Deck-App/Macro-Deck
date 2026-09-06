using System.Text.Json;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Testing;

/// <summary>
/// What a capability invocation actually produced - the plugin's real reply, never a value synthesised
/// by the test host itself.
/// </summary>
public sealed record CapabilityInvocationOutcome
{
	private CapabilityInvocationOutcome()
	{
	}

	/// <summary>Whether the invocation succeeded. False for a wire-level failure, a timeout or a cancellation.</summary>
	public required bool Succeeded { get; init; }

	/// <summary>The success value, when there is one. Deserialize it with <see cref="DataAs{T}" />.</summary>
	public JsonElement? Data { get; init; }

	/// <summary>The failure, exactly as the plugin's reply carried it, when <see cref="Succeeded" /> is false.</summary>
	public ProtocolError? Error { get; init; }

	/// <summary>The id of the <c>capability.invoke</c> this answers.</summary>
	public required string CorrelationId { get; init; }

	/// <summary>How long the invocation took, from send to reply.</summary>
	public required TimeSpan Elapsed { get; init; }

	/// <summary>Deserializes <see cref="Data" /> as <typeparamref name="T" /> through the wire's own JSON
	/// options, or returns <c>default</c> when there is no data.</summary>
	public T? DataAs<T>() => Data is { } element ? element.Deserialize<T>(PluginProtocolJson.Options) : default;

	/// <summary>Builds a successful outcome.</summary>
	internal static CapabilityInvocationOutcome Success(string correlationId, JsonElement? data, TimeSpan elapsed)
		=> new() { Succeeded = true, Data = data, CorrelationId = correlationId, Elapsed = elapsed };

	/// <summary>Builds a failed outcome, carrying the plugin's error verbatim.</summary>
	internal static CapabilityInvocationOutcome Failure(string correlationId, ProtocolError error, TimeSpan elapsed)
		=> new() { Succeeded = false, Error = error, CorrelationId = correlationId, Elapsed = elapsed };
}
