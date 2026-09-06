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

	public StoreOperationError? Error { get; init; }

	public string? ErrorMessage { get; init; }

	public bool IsTerminal => State is StoreOperationState.Completed
		or StoreOperationState.Failed
		or StoreOperationState.Cancelled;

	public bool CanRetry => State is StoreOperationState.Failed or StoreOperationState.Cancelled;
}
