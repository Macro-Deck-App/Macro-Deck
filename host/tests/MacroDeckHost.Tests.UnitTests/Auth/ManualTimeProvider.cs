namespace MacroDeckHost.Tests.UnitTests.Auth;

internal sealed class ManualTimeProvider : TimeProvider
{
	private readonly List<ManualTimer> _timers = [];
	private readonly object _lock = new();
	private long _scheduled;
	private TaskCompletionSource? _scheduleSignal;

	public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

	public override DateTimeOffset GetUtcNow() => Now;

	public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
	{
		var timer = new ManualTimer(this, callback, state);
		timer.Change(dueTime, period);
		return timer;
	}

	// Counts every timer arming. A loop that parks on Task.Delay(_, timeProvider, _) arms exactly one
	// timer per iteration, so an increase past a previously captured count is a deterministic signal that
	// the iteration finished, with no dependency on how fast the machine schedules the continuation.
	public long ScheduledCount
	{
		get
		{
			lock (_lock)
			{
				return _scheduled;
			}
		}
	}

	public bool Advance(TimeSpan delta)
	{
		Now += delta;
		return DrainDueTimers();
	}

	public async Task WaitForScheduleAsync(long previousScheduledCount)
	{
		while (true)
		{
			Task signal;
			lock (_lock)
			{
				if (_scheduled > previousScheduledCount)
				{
					return;
				}

				_scheduleSignal ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
				signal = _scheduleSignal.Task;
			}

			await signal;
		}
	}

	private bool DrainDueTimers()
	{
		var fired = false;
		while (true)
		{
			ManualTimer? due;
			lock (_lock)
			{
				due = _timers.FirstOrDefault(t => t.IsActive && t.DueAt <= Now);
			}

			if (due is null)
			{
				return fired;
			}

			fired = true;
			due.Fire(Now);
		}
	}

	private void OnScheduled()
	{
		lock (_lock)
		{
			_scheduled++;
			_scheduleSignal?.TrySetResult();
			_scheduleSignal = null;
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
		private readonly ManualTimeProvider _owner;
		private readonly TimerCallback _callback;
		private readonly object? _state;
		private TimeSpan _period;
		private bool _disposed;

		public ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state)
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
			_owner.OnScheduled();
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
