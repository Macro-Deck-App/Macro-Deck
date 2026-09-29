using System.Collections.Concurrent;
using MacroDeckHost.Domain.Common;

namespace MacroDeckHost.Application.Timers;

public sealed class TimerWidgetStore
{
	// One tap can reach the host twice, through the widget's tree and a client's older trigger request; an
	// identical gesture this soon after the last one is that same tap.
	public static readonly TimeSpan RepeatWindow = TimeSpan.FromMilliseconds(300);

	private readonly TimeProvider _time;
	private readonly ConcurrentDictionary<Guid, Entry> _entries = new();
	private long _version;

	public TimerWidgetStore(TimeProvider time)
	{
		_time = time;
	}

	public event EventHandler<TimerWidgetChangedEventArgs>? Changed;

	public DateTimeOffset Now => _time.GetUtcNow();

	public bool Contains(Guid widgetId) => _entries.ContainsKey(widgetId);

	public IReadOnlyCollection<Guid> WidgetIds => [.. _entries.Keys];

	public TimerWidgetSnapshot? Get(Guid widgetId)
	{
		if (!_entries.TryGetValue(widgetId, out var entry))
		{
			return null;
		}

		lock (entry.Gate)
		{
			return entry.Snapshot;
		}
	}

	public TimerWidgetSnapshot Ensure(Guid widgetId, TimerWidgetConfig config)
	{
		ArgumentNullException.ThrowIfNull(config);

		var created = false;
		var entry = _entries.GetOrAdd(widgetId,
			id =>
			{
				created = true;

				return new Entry(id, config, _time.GetUtcNow(), NextVersion());
			});

		TimerWidgetSnapshot snapshot;
		var changed = created;

		lock (entry.Gate)
		{
			if (!created && entry.Config != config)
			{
				var kindChanged = entry.Config.Kind != config.Kind;
				entry.Config = config;

				if (kindChanged)
				{
					entry.LastEnteredSeconds = null;
					entry.ReturnToIdle(_time.GetUtcNow());
				}
				else if (entry.Phase == TimerWidgetPhase.Idle)
				{
					entry.ReturnToIdle(_time.GetUtcNow());
				}

				entry.Version = NextVersion();
				changed = true;
			}

			snapshot = entry.Snapshot;
		}

		if (changed)
		{
			RaiseChanged(widgetId);
		}

		return snapshot;
	}

	public bool Remove(Guid widgetId)
	{
		if (!_entries.TryRemove(widgetId, out _))
		{
			return false;
		}

		RaiseChanged(widgetId);

		return true;
	}

	public IReadOnlyList<TimerWidgetTransition> Gesture(Guid widgetId, TimerGesture gesture)
	{
		if (!_entries.TryGetValue(widgetId, out var entry))
		{
			return [];
		}

		var transitions = new List<TimerWidgetTransition>(2);

		lock (entry.Gate)
		{
			var now = _time.GetUtcNow();

			if (entry.LastGesture == gesture && now - entry.LastGestureAt < RepeatWindow)
			{
				return [];
			}

			entry.LastGesture = gesture;
			entry.LastGestureAt = now;

			// A press can land between the countdown reaching zero and the next tick noticing it: it has
			// finished for the user already, so it finishes first and the press then dismisses it.
			if (entry.TryFinish(now))
			{
				entry.Version = NextVersion();
				transitions.Add(new TimerWidgetTransition(entry.Snapshot, [WidgetTriggerTypes.CountdownFinished]));
			}

			var trigger = entry.Config.Kind == TimerWidgetKind.Countdown
				? ApplyCountdownGesture(entry, gesture, now, out var needsDuration)
				: ApplyStopwatchGesture(entry, gesture, now, out needsDuration);

			if (trigger is not null)
			{
				entry.Version = NextVersion();
				transitions.Add(new TimerWidgetTransition(entry.Snapshot, [trigger]));
			}
			else if (needsDuration)
			{
				transitions.Add(new TimerWidgetTransition(entry.Snapshot, []) { NeedsDuration = true });
			}
		}

		if (transitions.Any(transition => transition.Triggers.Count > 0))
		{
			RaiseChanged(widgetId);
		}

		return transitions;
	}

	public TimerWidgetTransition? StartWithDuration(Guid widgetId, int seconds)
	{
		if (!_entries.TryGetValue(widgetId, out var entry))
		{
			return null;
		}

		TimerWidgetTransition transition;

		lock (entry.Gate)
		{
			if (entry.Config is not { Kind: TimerWidgetKind.Countdown, AsksForDuration: true } ||
				entry.Phase != TimerWidgetPhase.Idle)
			{
				return null;
			}

			var clamped = Math.Clamp(seconds, TimerWidgetConfig.MinDurationSeconds, TimerWidgetConfig.MaxDurationSeconds);
			entry.LastEnteredSeconds = clamped;
			entry.DurationMs = clamped * 1000L;
			entry.Start(_time.GetUtcNow());
			entry.Version = NextVersion();
			transition = new TimerWidgetTransition(entry.Snapshot, [WidgetTriggerTypes.CountdownStarted]);
		}

		RaiseChanged(widgetId);

		return transition;
	}

	public IReadOnlyList<TimerWidgetTransition> Tick()
	{
		var transitions = new List<TimerWidgetTransition>();
		var now = _time.GetUtcNow();

		foreach (var entry in _entries.Values)
		{
			TimerWidgetTransition? transition = null;

			lock (entry.Gate)
			{
				if (entry.Phase != TimerWidgetPhase.Running)
				{
					continue;
				}

				if (entry.TryFinish(now))
				{
					entry.Version = NextVersion();
					transition = new TimerWidgetTransition(entry.Snapshot, [WidgetTriggerTypes.CountdownFinished]);
				}
				else if (entry.ShownSecond(now) != entry.LastShownSecond)
				{
					entry.LastShownSecond = entry.ShownSecond(now);
					entry.Version = NextVersion();
					transition = new TimerWidgetTransition(entry.Snapshot, []);
				}
			}

			if (transition is not null)
			{
				transitions.Add(transition);
				RaiseChanged(entry.WidgetId);
			}
		}

		return transitions;
	}

	private static string? ApplyCountdownGesture(Entry entry, TimerGesture gesture, DateTimeOffset now,
		out bool needsDuration)
	{
		needsDuration = false;

		switch (entry.Phase, gesture)
		{
			case (TimerWidgetPhase.Idle, TimerGesture.Press) when entry.DurationMs is null:
				needsDuration = true;

				return null;
			case (TimerWidgetPhase.Idle, TimerGesture.Press):
			case (TimerWidgetPhase.Paused, TimerGesture.Press):
				entry.Start(now);

				return WidgetTriggerTypes.CountdownStarted;
			case (TimerWidgetPhase.Running, TimerGesture.Press):
				entry.Pause(now);

				return WidgetTriggerTypes.CountdownPaused;
			case (TimerWidgetPhase.Running, TimerGesture.LongPress):
			case (TimerWidgetPhase.Paused, TimerGesture.LongPress):
				entry.ReturnToIdle(now);

				return WidgetTriggerTypes.CountdownReset;
			case (TimerWidgetPhase.Finished, _):
				entry.ReturnToIdle(now);

				return WidgetTriggerTypes.CountdownDismissed;
			default:
				return null;
		}
	}

	private static string? ApplyStopwatchGesture(Entry entry, TimerGesture gesture, DateTimeOffset now,
		out bool needsDuration)
	{
		needsDuration = false;

		switch (entry.Phase, gesture)
		{
			case (TimerWidgetPhase.Idle, TimerGesture.Press):
			case (TimerWidgetPhase.Paused, TimerGesture.Press):
				entry.Start(now);

				return WidgetTriggerTypes.StopwatchStarted;
			case (TimerWidgetPhase.Running, TimerGesture.Press):
				entry.Pause(now);

				return WidgetTriggerTypes.StopwatchPaused;
			case (TimerWidgetPhase.Running, TimerGesture.LongPress):
			case (TimerWidgetPhase.Paused, TimerGesture.LongPress):
				entry.ReturnToIdle(now);

				return WidgetTriggerTypes.StopwatchReset;
			default:
				return null;
		}
	}

	private long NextVersion() => Interlocked.Increment(ref _version);

	private void RaiseChanged(Guid widgetId) => Changed?.Invoke(this, new TimerWidgetChangedEventArgs(widgetId));

	private sealed class Entry
	{
		public Entry(Guid widgetId, TimerWidgetConfig config, DateTimeOffset now, long version)
		{
			WidgetId = widgetId;
			Config = config;
			Version = version;
			ReturnToIdle(now);
		}

		public Lock Gate { get; } = new();

		public Guid WidgetId { get; }

		public TimerWidgetConfig Config { get; set; }

		public TimerWidgetPhase Phase { get; private set; }

		public long? DurationMs { get; set; }

		public long ElapsedMs { get; private set; }

		public DateTimeOffset Anchor { get; private set; }

		public int? LastEnteredSeconds { get; set; }

		public long LastShownSecond { get; set; }

		public TimerGesture? LastGesture { get; set; }

		public DateTimeOffset LastGestureAt { get; set; }

		public long Version { get; set; }

		public TimerWidgetSnapshot Snapshot => new()
		{
			WidgetId = WidgetId,
			Kind = Config.Kind,
			Phase = Phase,
			DurationMs = DurationMs,
			ElapsedMs = ElapsedMs,
			Anchor = Anchor,
			LastEnteredSeconds = LastEnteredSeconds,
			Version = Version,
		};

		public void Start(DateTimeOffset now)
		{
			Phase = TimerWidgetPhase.Running;
			Anchor = now;
			LastShownSecond = ShownSecond(now);
		}

		public void Pause(DateTimeOffset now)
		{
			ElapsedMs = Snapshot.ElapsedAt(now);
			Phase = TimerWidgetPhase.Paused;
			Anchor = now;
		}

		public bool TryFinish(DateTimeOffset now)
		{
			if (!Snapshot.HasReachedEnd(now))
			{
				return false;
			}

			ElapsedMs = DurationMs ?? 0;
			Phase = TimerWidgetPhase.Finished;
			Anchor = now;

			return true;
		}

		public void ReturnToIdle(DateTimeOffset now)
		{
			Phase = TimerWidgetPhase.Idle;
			ElapsedMs = 0;
			Anchor = now;
			DurationMs = Config is { Kind: TimerWidgetKind.Countdown, AsksForDuration: false }
				? Config.DurationSeconds * 1000L
				: null;
			LastShownSecond = 0;
		}

		public long ShownSecond(DateTimeOffset now)
			=> Config.Kind == TimerWidgetKind.Countdown
				? Snapshot.RemainingSecondsAt(now)
				: Snapshot.ElapsedSecondsAt(now);
	}
}
