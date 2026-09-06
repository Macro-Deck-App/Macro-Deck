using System.Collections.Concurrent;
using MacroDeck.Plugin.Protocol.Limits;

namespace MacroDeckHost.Application.Plugins.Logging;

public interface IPluginLogRateLimiter
{
	bool TryAcquire(string pluginId);

	void Evict(string pluginId);
}

public sealed class PluginLogRateLimiter : IPluginLogRateLimiter
{
	private readonly TimeProvider _timeProvider;
	private readonly ConcurrentDictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);

	public PluginLogRateLimiter(TimeProvider timeProvider)
	{
		_timeProvider = timeProvider;
	}

	public bool TryAcquire(string pluginId)
	{
		var bucket = _buckets.GetOrAdd(pluginId,
			static (_, state) => new Bucket(ProtocolLimits.MaxLogEventBurst, state.GetUtcNow()),
			_timeProvider);

		lock (bucket)
		{
			Refill(bucket);

			if (bucket.Tokens < 1d)
			{
				return false;
			}

			bucket.Tokens -= 1d;
			return true;
		}
	}

	public void Evict(string pluginId) => _buckets.TryRemove(pluginId, out _);

	private void Refill(Bucket bucket)
	{
		var now = _timeProvider.GetUtcNow();
		var elapsed = now - bucket.LastRefillAt;
		if (elapsed <= TimeSpan.Zero)
		{
			return;
		}

		bucket.Tokens = Math.Min(ProtocolLimits.MaxLogEventBurst,
			bucket.Tokens + elapsed.TotalSeconds * ProtocolLimits.MaxLogEventsPerSecond);
		bucket.LastRefillAt = now;
	}

	private sealed class Bucket
	{
		public Bucket(double tokens, DateTimeOffset now)
		{
			Tokens = tokens;
			LastRefillAt = now;
		}

		public double Tokens { get; set; }

		public DateTimeOffset LastRefillAt { get; set; }
	}
}
