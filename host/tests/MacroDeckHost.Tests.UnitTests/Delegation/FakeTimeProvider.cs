namespace MacroDeckHost.Tests.UnitTests.Delegation;

internal sealed class FakeTimeProvider : TimeProvider
{
	private readonly List<ManualTimer> _timers = [];
	private readonly Lock _lock = new();

	public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

	public override DateTimeOffset GetUtcNow() => Now;

	public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
	{
		var timer = new ManualTimer(this, callback, state);
		timer.Change(dueTime, period);
		return timer;
	}

	public void Advance(TimeSpan delta)
	{
		Now += delta;
		DrainDueTimers();
	}

	private void DrainDueTimers()
	{
		while (true)
		{
			ManualTimer? due;
			lock (_lock)
			{
				due = _timers.FirstOrDefault(t => t.IsActive && t.DueAt <= Now);
			}

			if (due is null)
			{
				return;
			}

			due.Fire(Now);
		}
	}

	private void Register(ManualTimer timer)
	{
		lock (_lock)
		{
			_timers.Add(timer);
		}
	}

	private void Unregister(ManualTimer timer)
	{
		lock (_lock)
		{
			_timers.Remove(timer);
		}
	}

	private sealed class ManualTimer : ITimer
	{
		private readonly FakeTimeProvider _owner;
		private readonly TimerCallback _callback;
		private readonly object? _state;
		private TimeSpan _period;
		private bool _disposed;

		public ManualTimer(FakeTimeProvider owner, TimerCallback callback, object? state)
		{
			_owner = owner;
			_callback = callback;
			_state = state;
			_owner.Register(this);
		}

		public DateTimeOffset DueAt { get; private set; }

		public bool IsActive { get; private set; }

		public bool Change(TimeSpan dueTime, TimeSpan period)
		{
			if (_disposed)
			{
				return false;
			}

			_period = period;

			if (dueTime == Timeout.InfiniteTimeSpan)
			{
				IsActive = false;
				return true;
			}

			DueAt = _owner.Now + dueTime;
			IsActive = true;
			return true;
		}

		public void Fire(DateTimeOffset now)
		{
			if (_period == Timeout.InfiniteTimeSpan || _period == TimeSpan.Zero)
			{
				IsActive = false;
			}
			else
			{
				DueAt = now + _period;
			}

			_callback(_state);
		}

		public void Dispose()
		{
			_disposed = true;
			IsActive = false;
			_owner.Unregister(this);
		}

		public ValueTask DisposeAsync()
		{
			Dispose();
			return ValueTask.CompletedTask;
		}
	}
}
