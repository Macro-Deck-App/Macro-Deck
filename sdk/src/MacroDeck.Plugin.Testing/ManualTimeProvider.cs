namespace MacroDeck.Plugin.Testing;

/// <summary>
/// A clock a test moves by hand, so timeout, keepalive and reconnect-backoff behaviour is asserted
/// rather than waited out on real time.
///
/// <para>
/// A capability invocation's own deadline is not driven by this type - see
/// <see cref="CapabilityInvokeOptions" />'s remarks - so advancing this clock has no effect on how long
/// <c>InvokeAsync</c> waits. It does drive anything built on <see cref="TimeProvider" /> that a plugin
/// or the test host was configured to use instead of <see cref="TimeProvider.System" />, most notably
/// reconnect scheduling and <c>PeriodicTimer</c>-based keepalives.
/// </para>
///
/// <para>
/// Every <see cref="System.Threading.ITimer" /> this provider creates honours the real due-time/period
/// contract: a timer becomes due <c>dueTime</c> after it was created or last <see cref="System.Threading.ITimer.Change(TimeSpan,TimeSpan)" />'d,
/// and - unless <c>period</c> is <see cref="Timeout.InfiniteTimeSpan" /> or <see cref="TimeSpan.Zero" /> -
/// again every <c>period</c> after that. A due time at or before the current moment fires immediately,
/// exactly as <see cref="System.Threading.Timer" /> fires a zero <c>dueTime</c> without waiting for a
/// clock tick. <see cref="Advance" /> fires every timer whose due time (or, for a repeating timer, next
/// period boundary) has elapsed by the new time - once per boundary crossed, the same number of times
/// the equivalent span of real time would have produced.
/// </para>
/// </summary>
public sealed class ManualTimeProvider : TimeProvider
{
	private readonly List<ManualTimer> _timers = [];
	private readonly Lock _gate = new();
	private DateTimeOffset _now;

	/// <summary>Creates a clock starting at <paramref name="now" />.</summary>
	public ManualTimeProvider(DateTimeOffset now) => _now = now;

	/// <summary>Creates a clock starting at <see cref="DateTimeOffset.UnixEpoch" />.</summary>
	public ManualTimeProvider()
		: this(DateTimeOffset.UnixEpoch)
	{
	}

	/// <inheritdoc />
	public override DateTimeOffset GetUtcNow()
	{
		lock (_gate)
		{
			return _now;
		}
	}

	/// <summary>
	/// Moves the clock forward by <paramref name="by" />, then fires every timer whose due time (or next
	/// period boundary) is now at or before the new time - once per boundary crossed, so a single
	/// <see cref="Advance" /> spanning several periods of a repeating timer fires it that many times, the
	/// same as if that much real time had actually passed. A timer not yet due (including one created
	/// with <see cref="Timeout.InfiniteTimeSpan" /> and never <see cref="System.Threading.ITimer.Change(TimeSpan,TimeSpan)" />'d
	/// since) does not fire.
	/// </summary>
	public void Advance(TimeSpan by)
	{
		if (by < TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(by), by, "The clock cannot move backwards.");
		}

		DateTimeOffset now;
		ManualTimer[] snapshot;

		lock (_gate)
		{
			_now += by;
			now = _now;
			snapshot = [.. _timers];
		}

		// Fired outside the provider's own lock: a callback may itself create, change or dispose a
		// timer (including this one), which must not deadlock or corrupt _timers mid-iteration.
		foreach (var timer in snapshot)
		{
			timer.FireDueCallbacks(now);
		}
	}

	/// <inheritdoc />
	public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
	{
		ArgumentNullException.ThrowIfNull(callback);

		var timer = new ManualTimer(callback, state, dueTime, period, GetUtcNow, Remove);

		lock (_gate)
		{
			_timers.Add(timer);
		}

		return timer;
	}

	private void Remove(ManualTimer timer)
	{
		lock (_gate)
		{
			_timers.Remove(timer);
		}
	}

	/// <summary>
	/// A single scheduled callback, tracking its own due time and period against the owning provider's
	/// clock. Private to <see cref="ManualTimeProvider" />; nothing outside this file constructs or
	/// inspects one directly.
	/// </summary>
	private sealed class ManualTimer : ITimer
	{
		private readonly TimerCallback _callback;
		private readonly object? _state;
		private readonly Func<DateTimeOffset> _now;
		private readonly Action<ManualTimer> _remove;
		private readonly Lock _gate = new();

		private bool _disposed;
		private DateTimeOffset? _nextDueAt;
		private TimeSpan _period = Timeout.InfiniteTimeSpan;

		public ManualTimer(
			TimerCallback callback,
			object? state,
			TimeSpan dueTime,
			TimeSpan period,
			Func<DateTimeOffset> now,
			Action<ManualTimer> remove)
		{
			_callback = callback;
			_state = state;
			_now = now;
			_remove = remove;

			var createdAt = now();

			lock (_gate)
			{
				Rearm(dueTime, period, createdAt);
			}

			FireDueCallbacks(createdAt);
		}

		/// <inheritdoc cref="ITimer.Change" />
		public bool Change(TimeSpan dueTime, TimeSpan period)
		{
			var now = _now();

			lock (_gate)
			{
				if (_disposed)
				{
					return false;
				}

				Rearm(dueTime, period, now);
			}

			FireDueCallbacks(now);
			return true;
		}

		/// <summary>Invokes the callback once for every due-time/period boundary at or before <paramref name="now" />.</summary>
		public void FireDueCallbacks(DateTimeOffset now)
		{
			while (true)
			{
				TimerCallback callback;
				object? state;

				lock (_gate)
				{
					if (_disposed || _nextDueAt is not { } dueAt || dueAt > now)
					{
						return;
					}

					callback = _callback;
					state = _state;

					// Zero, negative or infinite period: one-shot - the documented meaning of both
					// System.Threading.Timer's period parameter and this type's own contract.
					_nextDueAt = _period > TimeSpan.Zero ? dueAt + _period : null;
				}

				callback(state);
			}
		}

		public void Dispose()
		{
			lock (_gate)
			{
				_disposed = true;
			}

			_remove(this);
		}

		public ValueTask DisposeAsync()
		{
			Dispose();
			return ValueTask.CompletedTask;
		}

		/// <summary>Recomputes the next due time from <paramref name="now" />. Caller must hold <see cref="_gate" />.</summary>
		private void Rearm(TimeSpan dueTime, TimeSpan period, DateTimeOffset now)
		{
			ValidateTimerArgument(dueTime, nameof(dueTime));
			ValidateTimerArgument(period, nameof(period));

			_period = period;
			_nextDueAt = dueTime == Timeout.InfiniteTimeSpan
				? null
				: now + (dueTime < TimeSpan.Zero ? TimeSpan.Zero : dueTime);
		}

		private static void ValidateTimerArgument(TimeSpan value, string paramName)
		{
			if (value < TimeSpan.Zero && value != Timeout.InfiniteTimeSpan)
			{
				throw new ArgumentOutOfRangeException(paramName,
					value,
					$"'{paramName}' must be non-negative or Timeout.InfiniteTimeSpan.");
			}
		}
	}
}
