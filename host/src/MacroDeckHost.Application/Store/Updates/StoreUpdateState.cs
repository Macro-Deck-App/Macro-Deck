namespace MacroDeckHost.Application.Store.Updates;

public sealed class StoreUpdateState : IStoreUpdateState
{
	private volatile IReadOnlyList<StoreAvailableUpdate> _current = [];

	public IReadOnlyList<StoreAvailableUpdate> Current => _current;

	public event Action? Changed;

	public void Swap(IReadOnlyList<StoreAvailableUpdate> updates)
	{
		ArgumentNullException.ThrowIfNull(updates);
		_current = updates;
		Changed?.Invoke();
	}
}
