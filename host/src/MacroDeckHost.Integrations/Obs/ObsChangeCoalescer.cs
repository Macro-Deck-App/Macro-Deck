namespace MacroDeckHost.Integrations.Obs;

internal enum ObsTargetKind
{
	InputSettings,
	SourceActivity,
	Filter
}

internal sealed record ObsTargetChange(
	ObsTargetKind Kind,
	string Name,
	string? Child = null,
	IReadOnlyCollection<string>? Keys = null);

internal sealed class ObsChangeCoalescer : IDisposable
{
	public const int MaxKeysPerTarget = 64;

	private readonly Action<IReadOnlyList<ObsTargetChange>> _flush;
	private readonly TimeSpan _window;
	private readonly TimeProvider _time;
	private readonly Lock _gate = new();
	private Dictionary<(ObsTargetKind, string, string?), HashSet<string>> _pending = new();
	private ITimer? _timer;
	private bool _disposed;

	public ObsChangeCoalescer(Action<IReadOnlyList<ObsTargetChange>> flush, TimeSpan window, TimeProvider? time = null)
	{
		_flush = flush;
		_window = window;
		_time = time ?? TimeProvider.System;
	}

	public void Add(ObsTargetChange change)
	{
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			var key = (change.Kind, change.Name, change.Child);
			if (!_pending.TryGetValue(key, out var keys))
			{
				_pending[key] = keys = new HashSet<string>(StringComparer.Ordinal);
			}

			foreach (var name in change.Keys ?? [])
			{
				if (keys.Count >= MaxKeysPerTarget)
				{
					break;
				}

				keys.Add(name);
			}

			_timer ??= _time.CreateTimer(_ => Flush(), null, _window, Timeout.InfiniteTimeSpan);
		}
	}

	public void Dispose()
	{
		lock (_gate)
		{
			_disposed = true;
			_pending.Clear();
			_timer?.Dispose();
			_timer = null;
		}
	}

	private void Flush()
	{
		List<ObsTargetChange> batch;
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			batch = _pending
				.Select(entry => new ObsTargetChange(entry.Key.Item1, entry.Key.Item2, entry.Key.Item3, entry.Value))
				.ToList();
			_pending = new Dictionary<(ObsTargetKind, string, string?), HashSet<string>>();
			_timer?.Dispose();
			_timer = null;
		}

		_flush(batch);
	}
}
