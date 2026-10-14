using System.Globalization;

namespace MacroDeckHost.Integrations.Streaming;

public sealed record StreamStatsAccount(string AccountId, string Label, string VariablePrefix);

public interface IStreamStatsSource
{
	event EventHandler? Changed;

	IReadOnlyList<StreamStatsAccount> Accounts { get; }

	string? Read(StreamStatsAccount account, string variableName);
}

public sealed class StreamStatsSource : IStreamStatsSource
{
	private readonly Func<StreamStatsAccount, string, object?> _read;
	private readonly Lock _sync = new();

	private IReadOnlyList<StreamStatsAccount> _accounts = [];

	public StreamStatsSource(Func<StreamStatsAccount, string, object?> read)
	{
		_read = read;
	}

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

	public string? Read(StreamStatsAccount account, string variableName)
	{
		ArgumentNullException.ThrowIfNull(account);

		return Format(_read(account, variableName));
	}

	internal static string? Format(object? value)
	{
		return value switch
		{
			null => null,
			bool flag => flag ? "true" : "false",
			string text => text,
			IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
			_ => value.ToString()
		};
	}
}
