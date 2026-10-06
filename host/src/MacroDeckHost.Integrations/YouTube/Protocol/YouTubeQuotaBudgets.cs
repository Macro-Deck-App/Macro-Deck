using System.Collections.Concurrent;

namespace MacroDeckHost.Integrations.YouTube.Protocol;

internal sealed class YouTubeQuotaBudgets
{
	private readonly ConcurrentDictionary<string, YouTubeQuotaBudget> _budgets = new(StringComparer.Ordinal);
	private readonly TimeProvider _time;

	public YouTubeQuotaBudgets(TimeProvider? timeProvider = null)
	{
		_time = timeProvider ?? TimeProvider.System;
	}

	// Google counts quota per Cloud project, so every account sharing a client id draws on one budget.
	public static YouTubeQuotaBudgets Shared { get; } = new();

	public YouTubeQuotaBudget For(string clientId, int dailyLimit = YouTubeQuotaBudget.DefaultDailyLimit)
	{
		ArgumentException.ThrowIfNullOrEmpty(clientId);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dailyLimit);

		var budget = _budgets.GetOrAdd(clientId, _ => new YouTubeQuotaBudget(dailyLimit, _time));
		budget.SetLimit(dailyLimit);
		return budget;
	}
}
