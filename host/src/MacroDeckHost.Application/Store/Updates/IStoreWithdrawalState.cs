namespace MacroDeckHost.Application.Store.Updates;

public interface IStoreWithdrawalState
{
	IReadOnlyList<StoreInstalledWithdrawal> Current { get; }

	void Swap(IReadOnlyList<StoreInstalledWithdrawal> withdrawals);

	event Action? Changed;
}
