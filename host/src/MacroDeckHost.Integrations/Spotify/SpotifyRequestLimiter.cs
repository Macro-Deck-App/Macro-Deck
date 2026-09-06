namespace MacroDeckHost.Integrations.Spotify;

internal sealed class SpotifyRequestLimiter
{
	internal const double DefaultCeilingPerSecond = 2;

	private const double FloorPerSecond = 0.25;

	private const double RecoveryPerAcceptedRequest = 0.05;

	private const int DefaultBurst = 8;

	private const int InteractiveReserve = 3;

	private static readonly TimeSpan _maxWait = TimeSpan.FromSeconds(10);

	private readonly Lock _sync = new();
	private readonly double _ceilingPerSecond;
	private readonly double _burst;
	private readonly Func<DateTime> _utcNow;

	private double _permitsPerSecond;
	private double _permits;
	private DateTime _refilledAtUtc;

	internal SpotifyRequestLimiter(
		double? ceilingPerSecond = null,
		int? burst = null,
		Func<DateTime>? utcNow = null)
	{
		_ceilingPerSecond = ceilingPerSecond ?? DefaultCeilingPerSecond;
		_burst = burst ?? DefaultBurst;
		_utcNow = utcNow ?? (() => DateTime.UtcNow);
		_permitsPerSecond = _ceilingPerSecond;
		_permits = _burst;
		_refilledAtUtc = _utcNow();
	}

	internal double PermitsPerSecond
	{
		get
		{
			lock (_sync)
			{
				return _permitsPerSecond;
			}
		}
	}

	internal TimeSpan? Reserve(bool droppable)
	{
		lock (_sync)
		{
			Refill();

			if (droppable && _permits < 1 + InteractiveReserve)
			{
				return null;
			}

			_permits -= 1;
			if (_permits >= 0)
			{
				return TimeSpan.Zero;
			}

			var wait = TimeSpan.FromSeconds(-_permits / _permitsPerSecond);
			return wait < _maxWait ? wait : _maxWait;
		}
	}

	internal void NoteRefused()
	{
		lock (_sync)
		{
			Refill();
			_permitsPerSecond = Math.Max(FloorPerSecond, _permitsPerSecond / 2);
			_permits = Math.Min(_permits, 0);
		}
	}

	internal void NoteAccepted()
	{
		lock (_sync)
		{
			_permitsPerSecond = Math.Min(_ceilingPerSecond, _permitsPerSecond + RecoveryPerAcceptedRequest);
		}
	}

	private void Refill()
	{
		var now = _utcNow();
		var elapsed = now - _refilledAtUtc;
		if (elapsed <= TimeSpan.Zero)
		{
			// A clock correction (or a resume that stepped the clock back) must not mint permits.
			_refilledAtUtc = now;
			return;
		}

		_refilledAtUtc = now;
		_permits = Math.Min(_burst, _permits + (elapsed.TotalSeconds * _permitsPerSecond));
	}
}

internal static class SpotifyRequestScope
{
	private static readonly AsyncLocal<SpotifyRequestContext?> _context = new();

	internal static SpotifyRequestContext Current
		=> _context.Value ?? new SpotifyRequestContext(false, "unspecified", "runtime", "interactive");

	internal static bool IsDroppable => Current.Droppable;

	internal static IDisposable Poll(string reason = "scheduled-playback-state")
	{
		var previous = _context.Value;
		_context.Value = new SpotifyRequestContext(true, reason, "runtime", "poll");
		return new Restore(previous);
	}

	internal static IDisposable Interactive(string reason = "playback-action", string source = "runtime")
	{
		var previous = _context.Value;
		_context.Value = new SpotifyRequestContext(false, reason, source, "interactive");
		return new Restore(previous);
	}

	private sealed class Restore(SpotifyRequestContext? previous) : IDisposable
	{
		public void Dispose() => _context.Value = previous;
	}
}

internal sealed record SpotifyRequestContext(bool Droppable, string Reason, string Source, string Category);

internal sealed class SpotifyThrottledException : Exception
{
	public SpotifyThrottledException()
	{
	}

	public SpotifyThrottledException(string message)
		: base(message)
	{
	}

	public SpotifyThrottledException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
