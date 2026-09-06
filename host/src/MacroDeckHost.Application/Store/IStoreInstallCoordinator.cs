using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Application.Store.Operations;

namespace MacroDeckHost.Application.Store;

/// <summary>The store's install API surface: at most one live operation exists per (kind, packageId) at
/// any time, so a duplicate <see cref="Install" /> call is answered with the operation already in
/// flight rather than starting a second one.</summary>
public interface IStoreInstallCoordinator
{
	/// <summary><paramref name="allowUnsigned" /> records that the user accepted an explicit unsigned
	/// warning for this one install. It is a request, not a permission: the host still refuses unless
	/// developer mode is on and the package is unsigned rather than failing verification, and it applies
	/// to plugins only. A retry does not inherit it.</summary>
	StoreOperation Install(StoreExtensionKind kind,
		string packageId,
		string? version = null,
		bool allowUnsigned = false);

	StoreOperation? Retry(Guid operationId);

	bool Cancel(Guid operationId);

	bool Dismiss(Guid operationId);
}
