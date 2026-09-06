namespace MacroDeckHost.Application.Store.Installation;

public enum StoreUninstallError
{
	NotInstalled,
	DependencyInUse,
	LastProfileProtected,
	OperationInProgress,
	Failed
}
