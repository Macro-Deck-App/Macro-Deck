using System.Text.Json;
using MacroDeck.Plugin.Protocol.Errors;

namespace MacroDeck.Plugin.Protocol.Envelope;

/// <summary>
/// The one shape every message on the wire takes. <c>payload</c> and <see cref="Error" /> are
/// mutually exclusive. Unknown optional fields are ignored on read, never echoed back - see
/// <c>PluginProtocolJson</c>'s <c>UnmappedMemberHandling.Skip</c>.
/// </summary>
public sealed record ProtocolEnvelope
{
	/// <summary><c>&lt;domain&gt;.&lt;verb&gt;</c>, e.g. <c>capability.invoke</c>.</summary>
	public required string Type { get; init; }

	/// <summary>UUIDv7, minted by the sender.</summary>
	public required string Id { get; init; }

	public string? CorrelationId { get; init; }

	/// <summary>RFC 3339 UTC. Informational only - clocks differ between host and plugin process.</summary>
	public DateTimeOffset? SentAt { get; init; }

	public int? ProtocolVersion { get; init; }

	public int? DeadlineMs { get; init; }

	public string? IdempotencyKey { get; init; }

	// Deserializing straight into a JsonElement-typed property pins the backing document to the
	// element itself, so this stays valid after the surrounding JsonSerializer.Deserialize call
	// returns; it does not need - and cannot correctly receive - a manual Clone().
	public JsonElement? Payload { get; init; }

	public ProtocolError? Error { get; init; }
}
