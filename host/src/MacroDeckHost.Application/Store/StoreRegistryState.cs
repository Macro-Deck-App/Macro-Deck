namespace MacroDeckHost.Application.Store;

// Persisted per origin. AcceptedSequence is the anti-rollback high-water mark and must only ever
// move forward, including across restarts and across a cached tree reloaded from disk.
public sealed record StoreRegistryState
{
	public required string Origin { get; init; }

	public long AcceptedSequence { get; init; }

	public string? ManifestSha256 { get; init; }

	public string? CertificateId { get; init; }

	public DateTimeOffset? SignedAt { get; init; }

	public DateTimeOffset? LastSuccessAt { get; init; }
}
