using System.Collections.Concurrent;

namespace MacroDeckHost.Plugins.Capabilities.Callbacks;

public sealed class HostCallbackThrottle
{
	private readonly TimeProvider _timeProvider;
	private readonly int _capacity;
	private readonly double _refillPerSecond;
	private readonly ConcurrentDictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);

	public HostCallbackThrottle(TimeProvider timeProvider, int capacity = 20, double refillPerSecond = 10)
	{
		_timeProvider = timeProvider;
		_capacity = capacity;
		_refillPerSecond = refillPerSecond;
	}

	public bool TryConsume(string pluginId)
	{
		var now = _timeProvider.GetUtcNow();
		var bucket = _buckets.GetOrAdd(pluginId, _ => new Bucket(_capacity, now));

		lock (bucket)
		{
			var elapsedSeconds = (now - bucket.LastRefillAt).TotalSeconds;
			if (elapsedSeconds > 0)
			{
				bucket.Tokens = Math.Min(_capacity, bucket.Tokens + (elapsedSeconds * _refillPerSecond));
				bucket.LastRefillAt = now;
			}

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
