using MacroDeckHost.Application.Store.Model;

namespace MacroDeckHost.Application.Store.Operations;

public interface IStoreOperationTracker
{
	IReadOnlyList<StoreOperation> Snapshot();

	StoreOperation? Find(Guid operationId);

	StoreOperation? FindLive(StoreExtensionKind kind, string packageId);

	StoreOperation Create(StoreOperationKind kind,
		StoreExtensionKind extensionKind,
		string packageId,
		string version,
		string displayName,
		string? previousVersion,
		Guid? retryOf = null);

	StoreOperation? Transition(Guid operationId,
		StoreOperationState state,
		StoreOperationError? failure = null,
		string? errorMessage = null);

	void ReportProgress(Guid operationId, long bytesDownloaded, long? totalBytes);

	bool Dismiss(Guid operationId);

	void Restore(IReadOnlyList<StoreOperation> operations);

	event Action<StoreOperation>? Changed;
}
