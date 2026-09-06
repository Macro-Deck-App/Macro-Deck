using MacroDeckHost.Application.Store;
using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Application.Store.Operations;

namespace MacroDeckHost.Infrastructure.Store;

public sealed class StoreInstallCoordinator : IStoreInstallCoordinator
{
	private readonly IStoreCatalogQueryService _catalogQuery;
	private readonly IStoreOperationTracker _tracker;
	private readonly StoreOperationChannel _channel;
	private readonly StoreOperationCancellation _cancellation;
	private readonly StoreInstallConsent _consent;

	public StoreInstallCoordinator(IStoreCatalogQueryService catalogQuery,
		IStoreOperationTracker tracker,
		StoreOperationChannel channel,
		StoreOperationCancellation cancellation,
		StoreInstallConsent consent)
	{
		_catalogQuery = catalogQuery;
		_tracker = tracker;
		_channel = channel;
		_cancellation = cancellation;
		_consent = consent;
	}

	public StoreOperation Install(StoreExtensionKind kind,
		string packageId,
		string? version = null,
		bool allowUnsigned = false)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

		// A duplicate call is answered with the operation already in flight rather than starting a
		// second one: two installs of the same package racing each other would otherwise both try to
		// write the same icon pack or plugin version at once.
		var live = _tracker.FindLive(kind, packageId);
		if (live is not null)
		{
			return live;
		}

		var found = _catalogQuery.Find(kind, packageId);
		var item = found.Success ? found.Data : null;
		var resolvedVersion = version ?? item?.Entry.LatestVersion ?? "unknown";
		var displayName = item?.Entry.Name ?? packageId;
		var previousVersion = item?.InstalledVersion;
		var operationKind = previousVersion is not null ? StoreOperationKind.Update : StoreOperationKind.Install;

		var operation = _tracker.Create(operationKind,
			kind,
			packageId,
			resolvedVersion,
			displayName,
			previousVersion);

		// Recorded against this operation id alone, so it reaches the worker without being persisted with
		// the operation and without a retry - which creates a new operation - inheriting it.
		if (allowUnsigned)
		{
			_consent.Record(operation.Id);
		}

		_channel.Writer.TryWrite(operation.Id);
		return operation;
	}

	public StoreOperation? Retry(Guid operationId)
	{
		var operation = _tracker.Find(operationId);
		if (operation is null || !operation.CanRetry)
		{
			return null;
		}

		var next = _tracker.Create(operation.Kind,
			operation.ExtensionKind,
			operation.PackageId,
			operation.Version,
			operation.DisplayName,
			operation.PreviousVersion,
			retryOf: operationId);
		_channel.Writer.TryWrite(next.Id);
		return next;
	}

	public bool Cancel(Guid operationId)
	{
		var operation = _tracker.Find(operationId);
		if (operation is null || operation.IsTerminal)
		{
			return false;
		}

		if (_cancellation.Cancel(operationId))
		{
			return true;
		}

		// Nothing is running yet - the operation is still sitting in the channel - so it is cancelled
		// directly rather than waiting for the worker to pick it up and notice.
		_tracker.Transition(operationId,
			StoreOperationState.Cancelled,
			StoreOperationError.Cancelled,
			"Cancelled before it started.");
		return true;
	}

	public bool Dismiss(Guid operationId) => _tracker.Dismiss(operationId);
}
