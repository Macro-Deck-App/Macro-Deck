namespace MacroDeckHost.Application.Store.Installation;

public interface IStoreInstallationReconciler
{
	void PruneOrphanedIconPackRecords();
}
