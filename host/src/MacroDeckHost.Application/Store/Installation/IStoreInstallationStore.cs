using MacroDeckHost.Application.Store.Model;

namespace MacroDeckHost.Application.Store.Installation;

public interface IStoreInstallationStore
{
	IReadOnlyList<StoreInstallationRecord> LoadAll();

	StoreInstallationRecord? Find(StoreExtensionKind kind, string packageId);

	void Save(StoreInstallationRecord record);

	void Delete(StoreExtensionKind kind, string packageId);
}
