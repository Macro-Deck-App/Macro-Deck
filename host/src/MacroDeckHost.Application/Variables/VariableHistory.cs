using System.Globalization;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Variables;

/// <summary>
/// Rolling per-variable value windows, held in memory only and never persisted.
///
/// <para>
/// Sampled on a fixed tick rather than driven by variable changes: a value that has not moved still has to
/// advance the window, or a flat metric would draw as one long segment and a busy one would compress
/// however many changes happened to arrive. A fixed cadence also makes the samples evenly spaced, which is
/// what lets a reader draw them without timestamps.
/// </para>
///
/// <para>
/// A window's buffer outlives its last holder but stops being sampled, so leaving a folder does not keep
/// every variable a graph ever named ticking forever. Reopening backfills the gap on the original grid
/// (see <see cref="Resume" />) so a graph that was away for a while reads as continuous rather than as a
/// step.
/// </para>
/// </summary>
public sealed class VariableHistory : IVariableHistory, IDisposable
{
	/// <summary>The sampling cadence. One second, matching the rate the deck's own metrics move at.</summary>
	public static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(1);

	private readonly Lock _gate = new();
	private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
	private readonly VariableRegistry _variables;
	private readonly TimeProvider _timeProvider;
	private ITimer? _timer;

	public VariableHistory(VariableRegistry variables, TimeProvider timeProvider)
	{
		ArgumentNullException.ThrowIfNull(variables);
		ArgumentNullException.ThrowIfNull(timeProvider);

		_variables = variables;
		_timeProvider = timeProvider;
	}

	public IVariableHistoryWindow Open(string variableName, int capacity)
	{
		ArgumentException.ThrowIfNullOrEmpty(variableName);

		var bounded = Math.Max(1, capacity);
		Window window;

		lock (_gate)
		{
			if (!_entries.TryGetValue(variableName, out var entry))
			{
				entry = new Entry(variableName);
				_entries[variableName] = entry;
			}

			entry.Capacity = Math.Max(entry.Capacity, bounded);

			if (entry.Holders == 0)
			{
				Resume(entry);
			}

			entry.Holders++;
			window = new Window(this, entry);
			entry.Windows.Add(window);
			StartTimer();
		}

		return window;
	}

	public void Dispose()
	{
		lock (_gate)
		{
			_timer?.Dispose();
			_timer = null;
			_entries.Clear();
		}
	}

	private void Release(Window window)
	{
		lock (_gate)
		{
			if (!window.Entry.Windows.Remove(window))
			{
				return;
			}

			window.Entry.Holders = Math.Max(0, window.Entry.Holders - 1);

			if (_entries.Values.Any(entry => entry.Holders > 0))
			{
				return;
			}

			_timer?.Dispose();
			_timer = null;
		}
	}

	private void StartTimer()
		=> _timer ??= _timeProvider.CreateTimer(_ => Tick(), null, SampleInterval, SampleInterval);

	private void Tick()
	{
		List<Window>? changed = null;

		lock (_gate)
		{
			foreach (var entry in _entries.Values)
			{
				if (entry.Holders == 0 || !TrySample(entry, out var value))
				{
					continue;
				}

				Append(entry, value, _timeProvider.GetUtcNow());
				(changed ??= []).AddRange(entry.Windows);
			}
		}

		if (changed is null)
		{
			return;
		}

		// Raised outside the lock: a handler rebuilds a widget view, which is arbitrarily long work and
		// must not hold up the next tick or another session's subscribe.
		foreach (var window in changed)
		{
			window.RaiseChanged();
		}
	}

	/// <summary>
	/// Fills the gap since the last sample with the value that was standing, on the original tick grid,
	/// then seeds the live value at the current instant - so the window reads as if sampling had never
	/// stopped, ending on a fresh value rather than a repeated stale one.
	/// </summary>
	private void Resume(Entry entry)
	{
		if (!TrySample(entry, out var value))
		{
			return;
		}

		var now = _timeProvider.GetUtcNow();

		if (entry.LastSampledAt is { } last && entry.Values.Count > 0)
		{
			var elapsed = now - last;
			var missed = (int)Math.Floor(elapsed / SampleInterval);

			// A gap longer than the window can only contribute its most recent `Capacity` ticks; the rest
			// would be trimmed away anyway, so a variable left dormant for a day does not build a day of
			// synthetic samples first.
			var from = Math.Max(1, missed - entry.Capacity + 1);
			var standing = entry.Values[^1];

			for (var tick = from; tick <= missed; tick++)
			{
				Append(entry, standing, last + (SampleInterval * tick));
			}
		}

		Append(entry, value, now);
	}

	private static void Append(Entry entry, double value, DateTimeOffset at)
	{
		entry.Values.Add(value);
		entry.LastSampledAt = at;

		if (entry.Values.Count > entry.Capacity)
		{
			entry.Values.RemoveRange(0, entry.Values.Count - entry.Capacity);
		}
	}

	private bool TrySample(Entry entry, out double value)
	{
		value = 0;

		var variable = _variables.FindByName(VariableScope.Global, null, entry.Name);

		if (variable is null || !_variables.IsAvailable(variable.Id))
		{
			return false;
		}

		return double.TryParse(variable.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
			double.IsFinite(value);
	}

	private sealed class Entry
	{
		public Entry(string name) => Name = name;

		public string Name { get; }

		public List<double> Values { get; } = [];

		public List<Window> Windows { get; } = [];

		public int Capacity { get; set; } = 1;

		public int Holders { get; set; }

		public DateTimeOffset? LastSampledAt { get; set; }
	}

	private sealed class Window : IVariableHistoryWindow
	{
		private readonly VariableHistory _owner;
		private bool _released;

		public Window(VariableHistory owner, Entry entry)
		{
			_owner = owner;
			Entry = entry;
		}

		public event EventHandler? Changed;

		public Entry Entry { get; }

		public IReadOnlyList<double> Values
		{
			get
			{
				lock (_owner._gate)
				{
					return Entry.Values.ToArray();
				}
			}
		}

		public void Dispose()
		{
			if (_released)
			{
				return;
			}

			_released = true;
			_owner.Release(this);
		}

		public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
	}
}
