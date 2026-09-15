namespace MacroDeckHost.Application.Store;

public interface IStoreRegistryRefreshTracker
{
	StoreRegistryRefreshRun? Current { get; }

	event Action<StoreRegistryRefreshRun>? Changed;

	StoreRegistryRefreshRun Begin(StoreRegistryRefreshTrigger trigger);

	void Log(StoreRegistryRefreshStep reached, int? count = null, long? sequence = null);

	void ReportProgress(int filesCompleted, int filesTotal);

	void Finish(StoreRegistryRefreshRunState state,
		RegistryRefreshError? failure,
		string? detail,
		StoreRegistryStatus status);
}
