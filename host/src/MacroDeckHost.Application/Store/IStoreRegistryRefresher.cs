using MacroDeckHost.Domain.Common;

namespace MacroDeckHost.Application.Store;

public interface IStoreRegistryRefresher
{
	StoreRegistryStatus Status { get; }

	Task<Result<RegistryRefreshError>> Refresh(CancellationToken cancellationToken = default);

	Task<Result<RegistryRefreshError>> Refresh(StoreRegistryRefreshTrigger trigger,
		CancellationToken cancellationToken = default);

	Task LoadCachedRegistry(CancellationToken cancellationToken = default);
}
