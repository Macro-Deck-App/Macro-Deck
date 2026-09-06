using System.Text.Json.Serialization;

namespace MacroDeck.Signing.Registry;

/// <summary>The <c>registry-signature.json</c> wire document: a detached signature over the exact bytes of
/// <c>registry-manifest.json</c>, made with a <c>registry</c>-usage certificate.</summary>
internal sealed record RegistrySignatureDocument
{
	[JsonPropertyName("$schema")]
	public string? Schema { get; init; }

	public int SchemaVersion { get; init; } = 1;

	public string Algorithm { get; init; } = "ed25519";

	public required string KeyId { get; init; }

	public required string Value { get; init; }

	public required DateTimeOffset SignedAt { get; init; }
}
