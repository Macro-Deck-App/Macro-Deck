using System.Text.Json.Serialization;

namespace MacroDeck.Signing.Certificates;

/// <summary>
/// The DTO for <c>cert_&lt;id&gt;.json</c>, matching <c>macrodeck-certificate-v1.schema.json</c> exactly.
/// <see cref="SigningCertificateChain"/> is the only supported way to obtain a certificate this library
/// will act on: deserializing this type on its own gives no assurance the document was ever signed by the
/// Macro Deck root key.
/// </summary>
public sealed record SigningCertificate
{
	[JsonPropertyName("$schema")]
	public string? Schema { get; init; }

	public required int SchemaVersion { get; init; }

	public required SigningCertificateSubject Subject { get; init; }

	public string Algorithm { get; init; } = "ed25519";

	public required string CertificateId { get; init; }

	/// <summary>The certificate's raw 32-byte Ed25519 public key, base64-encoded.</summary>
	public required string PublicKey { get; init; }

	/// <summary>What this certificate may sign, exclusively - always exactly one entry.</summary>
	public required IReadOnlyList<string> KeyUsage { get; init; }

	public required DateTimeOffset NotBefore { get; init; }

	public required DateTimeOffset NotAfter { get; init; }

	public required DateTimeOffset IssuedAt { get; init; }

	/// <summary>Identifies the Macro Deck root key that signed this certificate.</summary>
	public required string RootKeyId { get; init; }
}

/// <summary>Who a <see cref="SigningCertificate"/> was issued to.</summary>
public sealed record SigningCertificateSubject
{
	/// <summary><c>"creator"</c> or <c>"organization"</c> for a package certificate; <c>"service"</c> for
	/// Macro Deck's own registry signing certificate.</summary>
	public required string Kind { get; init; }

	/// <summary>The subject's Macro Deck Platform identifier.</summary>
	public required string Id { get; init; }

	/// <summary>Human-readable subject name, shown next to a verified package.</summary>
	public required string Name { get; init; }
}
