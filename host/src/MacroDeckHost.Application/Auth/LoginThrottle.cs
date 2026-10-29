namespace MacroDeckHost.Application.Auth;

public class LoginThrottle
{
	private const int DefaultFreeAttempts = 5;
	private const int DefaultMaxEntries = 4096;
	private const string LoginKeySuffix = "|login";
	private const string EnrollmentKeySuffix = "|device-enrollment";
	private static readonly TimeSpan _defaultBaseLockout = TimeSpan.FromSeconds(30);
	private static readonly TimeSpan _defaultMaxLockout = TimeSpan.FromMinutes(15);
	private static readonly TimeSpan _entryLifetime = TimeSpan.FromHours(1);

	private readonly TimeProvider _timeProvider;
	private readonly int _freeAttempts;
	private readonly TimeSpan _baseLockout;
	private readonly TimeSpan _maxLockout;
	private readonly int _maxEntries;
	private readonly Lock _lock = new();
	private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

	public LoginThrottle(
		TimeProvider timeProvider,
		int freeAttempts = DefaultFreeAttempts,
		TimeSpan? baseLockout = null,
		TimeSpan? maxLockout = null,
		int maxEntries = DefaultMaxEntries)
	{
		_timeProvider = timeProvider;
		_freeAttempts = freeAttempts;
		_baseLockout = baseLockout ?? _defaultBaseLockout;
		_maxLockout = maxLockout ?? _defaultMaxLockout;
		_maxEntries = maxEntries;
	}

	public static string LoginKey(string clientKey)
	{
		return clientKey + LoginKeySuffix;
	}

	public static string EnrollmentKey(string clientKey)
	{
		return clientKey + EnrollmentKeySuffix;
	}

	public bool IsThrottled(string key, out TimeSpan retryAfter)
	{
		lock (_lock)
		{
			var now = _timeProvider.GetUtcNow();
			PruneStaleEntries(now);
			retryAfter = RemainingLockout(key, now);
			return retryAfter > TimeSpan.Zero;
		}
	}

	// Check and count in one step, so a parallel burst cannot all pass the check before any failure is
	// recorded. The attempt counts as a failure until RegisterSuccess clears it.
	public bool TryReserve(string key, out TimeSpan retryAfter)
	{
		lock (_lock)
		{
			var now = _timeProvider.GetUtcNow();
			PruneStaleEntries(now);
			retryAfter = RemainingLockout(key, now);
			if (retryAfter > TimeSpan.Zero)
			{
				return false;
			}

			CountFailure(key, now);
			return true;
		}
	}

	public void RegisterFailure(string key)
	{
		lock (_lock)
		{
			CountFailure(key, _timeProvider.GetUtcNow());
		}
	}

	public void RegisterSuccess(string key)
	{
		lock (_lock)
		{
			_entries.Remove(key);
		}
	}

	public void ClearLogins()
	{
		lock (_lock)
		{
			foreach (var key in _entries.Keys.Where(key => key.EndsWith(LoginKeySuffix, StringComparison.Ordinal))
				.ToList())
			{
				_entries.Remove(key);
			}
		}
	}

	private TimeSpan RemainingLockout(string key, DateTimeOffset now)
	{
		if (!_entries.TryGetValue(key, out var entry) || entry.LockedUntil is not { } lockedUntil)
		{
			return TimeSpan.Zero;
		}

		var remaining = lockedUntil - now;
		return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
	}

	private void CountFailure(string key, DateTimeOffset now)
	{
		if (!_entries.ContainsKey(key) && _entries.Count >= _maxEntries)
		{
			MakeRoom(now);
		}

		var failures = _entries.TryGetValue(key, out var entry) ? entry.Failures + 1 : 1;
		DateTimeOffset? lockedUntil = null;
		if (failures >= _freeAttempts)
		{
			var factor = Math.Min(failures - _freeAttempts, 8);
			var lockout = TimeSpan.FromTicks(Math.Min(_baseLockout.Ticks * (1L << factor), _maxLockout.Ticks));
			lockedUntil = now + lockout;
		}

		_entries[key] = new Entry(failures, lockedUntil, now);
	}

	private void PruneStaleEntries(DateTimeOffset now)
	{
		var cutoff = now - _entryLifetime;
		foreach (var key in _entries.Where(pair => pair.Value.LastFailureAt < cutoff)
			.Select(pair => pair.Key)
			.ToList())
		{
			_entries.Remove(key);
		}
	}

	// Only per-client entries give way: a global key such as the pairing-code bucket caps guesses across every
	// address, and evicting it would hand a caller rotating addresses a fresh host-wide budget.
	private void MakeRoom(DateTimeOffset now)
	{
		PruneStaleEntries(now);
		if (_entries.Count < _maxEntries)
		{
			return;
		}

		var oldest = _entries.Where(pair => IsClientKey(pair.Key))
			.OrderBy(pair => pair.Value.LastFailureAt)
			.Select(pair => pair.Key)
			.FirstOrDefault();
		if (oldest is not null)
		{
			_entries.Remove(oldest);
		}
	}

	private static bool IsClientKey(string key)
	{
		return key.EndsWith(LoginKeySuffix, StringComparison.Ordinal)
			|| key.EndsWith(EnrollmentKeySuffix, StringComparison.Ordinal);
	}

	private sealed record Entry(int Failures, DateTimeOffset? LockedUntil, DateTimeOffset LastFailureAt);
}
