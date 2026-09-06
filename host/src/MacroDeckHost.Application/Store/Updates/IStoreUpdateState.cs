namespace MacroDeckHost.Application.Store.Updates;

/// <summary>The current list of available updates, held in memory so every surface (notifications, the
/// controller another worker owns) reads the same answer without recomputing it.</summary>
public interface IStoreUpdateState
{
	IReadOnlyList<StoreAvailableUpdate> Current { get; }

	void Swap(IReadOnlyList<StoreAvailableUpdate> updates);

	event Action? Changed;
}
