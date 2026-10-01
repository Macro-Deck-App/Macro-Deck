namespace MacroDeckHost.Application.Twitch.Stats;

public sealed record TwitchStatsAccount(string UserId, string Label, string VariableKey);

public interface ITwitchStatsAccounts
{
	event EventHandler? Changed;

	IReadOnlyList<TwitchStatsAccount> Accounts { get; }
}

public interface ITwitchStatsSink
{
	void SetAccounts(IReadOnlyList<TwitchStatsAccount> accounts);
}

public sealed class TwitchStatsAccountsHub : ITwitchStatsAccounts, ITwitchStatsSink
{
	private readonly Lock _sync = new();

	private IReadOnlyList<TwitchStatsAccount> _accounts = [];

	public event EventHandler? Changed;

	public IReadOnlyList<TwitchStatsAccount> Accounts
	{
		get
		{
			lock (_sync)
			{
				return _accounts;
			}
		}
	}

	public void SetAccounts(IReadOnlyList<TwitchStatsAccount> accounts)
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
