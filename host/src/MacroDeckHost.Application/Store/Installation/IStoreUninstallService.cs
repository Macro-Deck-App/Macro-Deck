using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Domain.Common;

namespace MacroDeckHost.Application.Store.Installation;

public interface IStoreUninstallService
{
	Task<Result<StoreUninstallError>> Uninstall(StoreExtensionKind kind,
		string packageId,
		CancellationToken cancellationToken = default);
}
