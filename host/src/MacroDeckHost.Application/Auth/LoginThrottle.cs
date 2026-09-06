using System.Collections.Concurrent;

namespace MacroDeckHost.Application.Auth;

public class LoginThrottle
{
	private const int DefaultFreeAttempts = 5;
	private static readonly TimeSpan _defaultBaseLockout = TimeSpan.FromSeconds(30);
	private static readonly TimeSpan _defaultMaxLockout = TimeSpan.FromMinutes(15);
	private static readonly TimeSpan _entryLifetime = TimeSpan.FromHours(1);

	private readonly TimeProvider _timeProvider;
	private readonly int _freeAttempts;
	private readonly TimeSpan _baseLockout;
	private readonly TimeSpan _maxLockout;
	private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

	public LoginThrottle(
		TimeProvider timeProvider,
		int freeAttempts = DefaultFreeAttempts,
		TimeSpan? baseLockout = null,
		TimeSpan? maxLockout = null)
	{
		_timeProvider = timeProvider;
		_freeAttempts = freeAttempts;
		_baseLockout = baseLockout ?? _defaultBaseLockout;
		_maxLockout = maxLockout ?? _defaultMaxLockout;
	}

	public bool IsThrottled(string key, out TimeSpan retryAfter)
	{
		retryAfter = TimeSpan.Zero;
		PruneStaleEntries();

		if (!_entries.TryGetValue(key, out var entry) || entry.LockedUntil is null)
		{
			return false;
		}

		var remaining = entry.LockedUntil.Value - _timeProvider.GetUtcNow();
		if (remaining <= TimeSpan.Zero)
		{
			return false;
		}

		retryAfter = remaining;
		return true;
	}

	public void RegisterFailure(string key)
	{
		var now = _timeProvider.GetUtcNow();
		_entries.AddOrUpdate(key,
			_ => new Entry(1, null, now),
			(_, entry) =>
			{
				var failures = entry.Failures + 1;
				DateTimeOffset? lockedUntil = null;
				if (failures >= _freeAttempts)
				{
					var factor = Math.Min(failures - _freeAttempts, 8);
					var lockout = TimeSpan.FromTicks(Math.Min(_baseLockout.Ticks * (1L << factor),
						_maxLockout.Ticks));
					lockedUntil = now + lockout;
				}

				return new Entry(failures, lockedUntil, now);
			});
	}

	public void RegisterSuccess(string key) => _entries.TryRemove(key, out _);

	private void PruneStaleEntries()
	{
		var cutoff = _timeProvider.GetUtcNow() - _entryLifetime;
		foreach (var (key, entry) in _entries)
		{
			if (entry.LastFailureAt < cutoff)
			{
				_entries.TryRemove(key, out _);
			}
		}
	}

	private sealed record Entry(int Failures, DateTimeOffset? LockedUntil, DateTimeOffset LastFailureAt);
}
