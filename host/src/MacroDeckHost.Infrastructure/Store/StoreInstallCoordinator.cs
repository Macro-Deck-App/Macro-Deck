using MacroDeckHost.Application.Store;
using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Application.Store.Operations;
using MacroDeckHost.Application.Store.Reviews;

namespace MacroDeckHost.Infrastructure.Store;

public sealed class StoreInstallCoordinator : IStoreInstallCoordinator
{
	private readonly IStoreCatalogQueryService _catalogQuery;
	private readonly IStoreOperationTracker _tracker;
	private readonly StoreOperationChannel _channel;
	private readonly StoreOperationCancellation _cancellation;
	private readonly StoreInstallConsent _consent;
	private readonly StoreInstallBackupBatches _backupBatches;

	public StoreInstallCoordinator(IStoreCatalogQueryService catalogQuery,
		IStoreOperationTracker tracker,
		StoreOperationChannel channel,
		StoreOperationCancellation cancellation,
		StoreInstallConsent consent,
		StoreInstallBackupBatches backupBatches)
	{
		_catalogQuery = catalogQuery;
		_tracker = tracker;
		_channel = channel;
		_cancellation = cancellation;
		_consent = consent;
		_backupBatches = backupBatches;
	}

	public StoreOperation Install(StoreExtensionKind kind,
		string packageId,
		string? version = null,
		bool allowUnsigned = false,
		string? backupBatchId = null)
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
			previousVersion,
			versionPinned: version is not null);

		// Recorded against this operation id alone, so it reaches the worker without being persisted with
		// the operation and without a retry - which creates a new operation - inheriting it.
		if (allowUnsigned)
		{
			_consent.Record(operation.Id);
		}

		if (backupBatchId is not null)
		{
			_backupBatches.Record(operation.Id, backupBatchId);
		}

		_channel.Writer.TryWrite(operation.Id);
		return operation;
	}

	public bool IsUnavailableVersion(StoreExtensionKind kind, string packageId, string version)
	{
		var found = _catalogQuery.Find(kind, packageId);
		return found.Success && found.Data!.Entry.FindRelease(version) is null;
	}

	public StoreOperation InstallTestBuild(string packageId,
		string displayName,
		StorePlatformTestBuild build,
		bool consent)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
		ArgumentNullException.ThrowIfNull(build);

		var live = _tracker.FindLive(StoreExtensionKind.Plugin, packageId);
		if (live is not null)
		{
			return live;
		}

		var operation = _tracker.Create(StoreOperationKind.TestInstall,
			StoreExtensionKind.Plugin,
			packageId,
			build.Version,
			displayName,
			previousVersion: null,
			testBuild: new StoreTestBuildReference(build.Id, build.Build));
		if (consent)
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
			retryOf: operationId,
			versionPinned: operation.VersionPinned);
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
