namespace MacroDeckHost.Application.Store.Updates;

public sealed class StoreWithdrawalState : IStoreWithdrawalState
{
	private volatile IReadOnlyList<StoreInstalledWithdrawal> _current = [];

	public IReadOnlyList<StoreInstalledWithdrawal> Current => _current;

	public event Action? Changed;

	public void Swap(IReadOnlyList<StoreInstalledWithdrawal> withdrawals)
	{
		ArgumentNullException.ThrowIfNull(withdrawals);
		if (_current.SequenceEqual(withdrawals))
		{
			return;
		}

		_current = withdrawals;
		Changed?.Invoke();
	}
}
