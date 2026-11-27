using System.Collections.Concurrent;

namespace MacroDeckHost.Plugins.Capabilities.Callbacks;

public sealed class KeyImageFetchThrottle
{
	public const int BurstRepaints = 2;

	public const int RepaintsPerSecond = 1;

	private readonly TimeProvider _timeProvider;
	private readonly ConcurrentDictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);

	public KeyImageFetchThrottle(TimeProvider timeProvider)
	{
		_timeProvider = timeProvider;
	}

	public bool TryConsume(string pluginId, int cells)
	{
		var sized = Math.Max(1, cells);
		double capacity = sized * BurstRepaints;
		double refillPerSecond = sized * RepaintsPerSecond;
		var now = _timeProvider.GetUtcNow();
		var bucket = _buckets.GetOrAdd(pluginId, _ => new Bucket(capacity, capacity, now));

		lock (bucket)
		{
			var elapsedSeconds = (now - bucket.LastRefillAt).TotalSeconds;
			if (elapsedSeconds > 0)
			{
				bucket.Tokens += elapsedSeconds * refillPerSecond;
				bucket.LastRefillAt = now;
			}

			// Newly opened cells get their first paint right away rather than after a refill.
			if (capacity > bucket.Capacity)
			{
				bucket.Tokens += capacity - bucket.Capacity;
			}

			bucket.Capacity = capacity;
			bucket.Tokens = Math.Min(capacity, bucket.Tokens);

			if (bucket.Tokens < 1)
			{
				return false;
			}

			bucket.Tokens -= 1;
			return true;
		}
	}

	private sealed class Bucket(double tokens, double capacity, DateTimeOffset lastRefillAt)
	{
		public double Tokens = tokens;

		public double Capacity = capacity;

		public DateTimeOffset LastRefillAt = lastRefillAt;
	}
}
