namespace MacroDeckHost.Application.Store.Testing;

public interface IStoreTestInstallationStore
{
	StoreTestInstallationRecord? Find(string pluginId);

	void Save(StoreTestInstallationRecord record);

	void Delete(string pluginId);
}
