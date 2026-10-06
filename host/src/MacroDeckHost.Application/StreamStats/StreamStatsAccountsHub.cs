namespace MacroDeckHost.Application.StreamStats;

public sealed record StreamStatsAccount(string AccountId, string Label, string VariablePrefix);

public interface IStreamStatsAccounts
{
	event EventHandler? Changed;

	IReadOnlyList<StreamStatsAccount> Accounts { get; }
}

public interface IStreamStatsSink
{
	void SetAccounts(IReadOnlyList<StreamStatsAccount> accounts);
}

public sealed class StreamStatsAccountsHub : IStreamStatsAccounts, IStreamStatsSink
{
	private readonly Lock _sync = new();

	private IReadOnlyList<StreamStatsAccount> _accounts = [];

	public event EventHandler? Changed;

	public IReadOnlyList<StreamStatsAccount> Accounts
	{
		get
		{
			lock (_sync)
			{
				return _accounts;
			}
		}
	}

	public void SetAccounts(IReadOnlyList<StreamStatsAccount> accounts)
	{
		ArgumentNullException.ThrowIfNull(accounts);

		lock (_sync)
		{
			if (_accounts.SequenceEqual(accounts))
			{
				return;
			}

			_accounts = [.. accounts];
		}

		Changed?.Invoke(this, EventArgs.Empty);
	}
}
