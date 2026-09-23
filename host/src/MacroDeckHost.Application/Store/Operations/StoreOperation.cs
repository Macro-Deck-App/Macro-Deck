using MacroDeckHost.Application.Store.Model;

namespace MacroDeckHost.Application.Store.Operations;

public sealed record StoreOperation
{
	public required Guid Id { get; init; }

	public required StoreOperationKind Kind { get; init; }

	public required StoreExtensionKind ExtensionKind { get; init; }

	public required string PackageId { get; init; }

	public required string Version { get; init; }

	public required string DisplayName { get; init; }

	public string? PreviousVersion { get; init; }

	public StoreOperationState State { get; init; } = StoreOperationState.Queued;

	public long BytesDownloaded { get; init; }

	public long? TotalBytes { get; init; }

	public int? EtaSeconds { get; init; }

	public DateTimeOffset StartedAt { get; init; }

	public DateTimeOffset UpdatedAt { get; init; }

	public DateTimeOffset? CompletedAt { get; init; }

	public Guid? RetryOf { get; init; }

	public Guid? RootOperationId { get; init; }

	public Guid? TestBuildId { get; init; }

	public string? TestBuild { get; init; }

	public StoreOperationError? Error { get; init; }

	public string? ErrorMessage { get; init; }

	// Only a version the user asked for is pinned: an unpinned operation installs whatever is latest
	// when it runs, so a retry after the registry moved on does not reinstall a stale release.
	public bool VersionPinned { get; init; }

	public string? HostVersion { get; init; }

	public bool IsTerminal => State is StoreOperationState.Completed
		or StoreOperationState.Failed
		or StoreOperationState.Cancelled;

	// A test install carries the consent given for it alone, so it is started again from the Tests tab rather than retried.
	public bool CanRetry => (State is StoreOperationState.Failed or StoreOperationState.Cancelled) &&
		Kind != StoreOperationKind.TestInstall &&
		(Error is not { } error || IsRetryable(error));

	public static bool IsRetryable(StoreOperationError error) => error is not (StoreOperationError.Incompatible
		or StoreOperationError.RequiresNewerMacroDeck
		or StoreOperationError.Unsupported
		or StoreOperationError.PackageRemoved
		or StoreOperationError.VersionNotFound
		or StoreOperationError.UnsignedNotPermitted
		or StoreOperationError.TrustDowngrade
		or StoreOperationError.SignatureInvalid
		or StoreOperationError.SignatureUntrusted
		or StoreOperationError.MalformedPackage
		or StoreOperationError.ArtifactTooLarge
		or StoreOperationError.TestConsentRequired
		or StoreOperationError.TestBuildMismatch);
}
