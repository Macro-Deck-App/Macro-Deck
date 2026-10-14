using System.Collections.Concurrent;

namespace MacroDeckHost.Plugins.Capabilities.Callbacks;

public sealed class VariableHistoryReadThrottle
{
	public const int ReadsPerWindowPerSecond = 2;

	public const int BurstPerWindow = 4;

	private readonly TimeProvider _timeProvider;
	private readonly ConcurrentDictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);

	public VariableHistoryReadThrottle(TimeProvider timeProvider)
	{
		_timeProvider = timeProvider;
	}

	public bool TryConsume(string pluginId, int openWindows)
	{
		var windows = Math.Max(1, openWindows);
		double capacity = windows * BurstPerWindow;
		double refillPerSecond = windows * ReadsPerWindowPerSecond;
		var now = _timeProvider.GetUtcNow();
		var bucket = _buckets.GetOrAdd(pluginId, _ => new Bucket(capacity, now));

		lock (bucket)
		{
			var elapsedSeconds = (now - bucket.LastRefillAt).TotalSeconds;
			if (elapsedSeconds > 0)
			{
				bucket.Tokens += elapsedSeconds * refillPerSecond;
				bucket.LastRefillAt = now;
			}

			bucket.Tokens = Math.Min(capacity, bucket.Tokens);

			if (bucket.Tokens < 1)
			{
				return false;
			}

			bucket.Tokens -= 1;
			return true;
		}
	}

	private sealed class Bucket(double tokens, DateTimeOffset lastRefillAt)
	{
		public double Tokens = tokens;

		public DateTimeOffset LastRefillAt = lastRefillAt;
	}
}
