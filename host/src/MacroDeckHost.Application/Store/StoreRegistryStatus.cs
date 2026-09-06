namespace MacroDeckHost.Application.Store;

public sealed record StoreRegistryStatus
{
	public static readonly StoreRegistryStatus Unavailable = new();

	public bool HasCatalog { get; init; }

	public long Sequence { get; init; }

	public DateTimeOffset? SignedAt { get; init; }

	public DateTimeOffset? FetchedAt { get; init; }

	public DateTimeOffset? LastSuccessAt { get; init; }

	public DateTimeOffset? LastAttemptAt { get; init; }

	public bool Refreshing { get; init; }

	public bool Stale { get; init; }

	public string? CertificateId { get; init; }

	public RegistryRefreshError? LastError { get; init; }

	public string? LastErrorMessage { get; init; }
}
