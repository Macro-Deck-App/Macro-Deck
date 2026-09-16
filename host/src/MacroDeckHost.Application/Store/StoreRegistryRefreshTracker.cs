namespace MacroDeckHost.Application.Store;

public sealed class StoreRegistryRefreshTracker : IStoreRegistryRefreshTracker
{
	private static readonly TimeSpan _progressInterval = TimeSpan.FromMilliseconds(250);

	private readonly Guid _hostInstanceId = Guid.NewGuid();
	private readonly Lock _lock = new();
	private readonly TimeProvider _timeProvider;

	private StoreRegistryRefreshRun? _current;
	private long _revision;
	private DateTimeOffset _lastProgressAt;

	public StoreRegistryRefreshTracker(TimeProvider timeProvider)
	{
		_timeProvider = timeProvider;
	}

	public StoreRegistryRefreshRun? Current
	{
		get
		{
			lock (_lock)
			{
				return _current;
			}
		}
	}

	public event Action<StoreRegistryRefreshRun>? Changed;

	public StoreRegistryRefreshRun Begin(StoreRegistryRefreshTrigger trigger)
	{
		lock (_lock)
		{
			var now = _timeProvider.GetUtcNow();
			_lastProgressAt = DateTimeOffset.MinValue;
			return Publish(new StoreRegistryRefreshRun
			{
				HostInstanceId = _hostInstanceId,
				Id = Guid.CreateVersion7(),
				Trigger = trigger,
				State = StoreRegistryRefreshRunState.Running,
				StartedAt = now,
				Entries = [new StoreRegistryRefreshLogEntry { At = now, Step = StoreRegistryRefreshStep.Started }]
			});
		}
	}

	public void Log(StoreRegistryRefreshStep reached, int? count = null, long? sequence = null)
	{
		lock (_lock)
		{
			if (_current is not { IsTerminal: false } run)
			{
				return;
			}

			if (reached == StoreRegistryRefreshStep.WaitingForRegistryUpdate)
			{
				_lastProgressAt = DateTimeOffset.MinValue;
				run = run with { FilesCompleted = 0, FilesTotal = 0 };
			}

			Publish(run with
			{
				Entries =
				[
					..run.Entries,
					new StoreRegistryRefreshLogEntry
					{
						At = _timeProvider.GetUtcNow(),
						Step = reached,
						Count = count,
						Sequence = sequence
					}
				]
			});
		}
	}

	public void ReportProgress(int filesCompleted, int filesTotal)
	{
		lock (_lock)
		{
			if (_current is not { IsTerminal: false } run)
			{
				return;
			}

			var now = _timeProvider.GetUtcNow();
			if (filesCompleted < filesTotal && now - _lastProgressAt < _progressInterval)
			{
				return;
			}

			_lastProgressAt = now;
			Publish(run with { FilesCompleted = filesCompleted, FilesTotal = filesTotal });
		}
	}

	public void Finish(StoreRegistryRefreshRunState state,
		RegistryRefreshError? failure,
		string? detail,
		StoreRegistryStatus status)
	{
		lock (_lock)
		{
			if (_current is not { IsTerminal: false } run)
			{
				return;
			}

			var now = _timeProvider.GetUtcNow();
			var closing = state switch
			{
				StoreRegistryRefreshRunState.Failed => new StoreRegistryRefreshLogEntry
				{
					At = now,
					Step = StoreRegistryRefreshStep.Failed,
					Error = failure,
					Detail = detail
				},
				StoreRegistryRefreshRunState.Cancelled => new StoreRegistryRefreshLogEntry
				{
					At = now,
					Step = StoreRegistryRefreshStep.Cancelled
				},
				_ => null
			};

			Publish(run with
			{
				State = state,
				FinishedAt = now,
				Status = status,
				Entries = closing is null ? run.Entries : [..run.Entries, closing]
			});
		}
	}

	// Raised inside the lock so the broadcast queue receives snapshots in revision order.
	private StoreRegistryRefreshRun Publish(StoreRegistryRefreshRun run)
	{
		_current = run with { Revision = ++_revision };
		Changed?.Invoke(_current);
		return _current;
	}
}
