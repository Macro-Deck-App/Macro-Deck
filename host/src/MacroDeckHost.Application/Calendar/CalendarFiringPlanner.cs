namespace MacroDeckHost.Application.Calendar;

public enum CalendarFiringKind
{
	StartsSoon,

	Started,

	Ended
}

public sealed record CalendarFiringTarget(
	CalendarFiringKind Kind,
	object Key,
	TimeSpan LeadTime = default,
	Func<CalendarEventSummary, bool>? Accepts = null,
	object? Context = null);

public sealed record CalendarFiring(CalendarFiringTarget Target, CalendarEventSummary Event, DateTimeOffset Instant);

public sealed record CalendarFiringPlan(IReadOnlyList<CalendarFiring> Due, DateTimeOffset? Next);

public sealed class CalendarFiringPlanner
{
	// An event created shortly before it starts is first seen at the next poll, so a passed instant stays
	// due for one poll interval plus slack; anything older is never replayed.
	public static readonly TimeSpan Freshness = CalendarTime.PollInterval + TimeSpan.FromMinutes(1);

	public static readonly TimeSpan StartsSoonTolerance = TimeSpan.FromMinutes(1);

	private readonly Dictionary<FiringKey, DateTimeOffset> _fired = [];
	private DateTimeOffset? _armedSince;

	public CalendarFiringPlan Plan(
		CalendarSnapshot snapshot,
		DateTimeOffset now,
		IReadOnlyCollection<CalendarFiringTarget> targets)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		ArgumentNullException.ThrowIfNull(targets);

		var due = new List<CalendarFiring>();
		DateTimeOffset? next = null;

		if (ReferenceEquals(snapshot, CalendarSnapshot.Empty))
		{
			return new CalendarFiringPlan(due, next);
		}

		// Started and ended instants that passed before the first snapshot or before the sync window are
		// never replayed, which also keeps the fired keys pruned below bounded.
		var floor = _armedSince is { } armed && armed > snapshot.WindowStart ? armed : snapshot.WindowStart;
		_armedSince = _armedSince is null ? now : floor;

		foreach (var stale in _fired.Where(entry => entry.Value < snapshot.WindowStart).Select(e => e.Key).ToList())
		{
			_fired.Remove(stale);
		}

		foreach (var calendarEvent in snapshot.Events)
		{
			foreach (var target in targets)
			{
				if (target.Accepts is { } accepts && !accepts(calendarEvent))
				{
					continue;
				}

				switch (target.Kind)
				{
					case CalendarFiringKind.Started:
						ConsiderPassed(new CalendarFiring(target, calendarEvent, calendarEvent.Start));
						break;
					case CalendarFiringKind.Ended:
						ConsiderPassed(new CalendarFiring(target, calendarEvent, calendarEvent.End));
						break;
					default:
						ConsiderStartsSoon(new CalendarFiring(target, calendarEvent, calendarEvent.Start - target.LeadTime));
						break;
				}
			}
		}

		return new CalendarFiringPlan(due, next);

		void ConsiderPassed(CalendarFiring firing)
		{
			if (firing.Instant > now)
			{
				Schedule(firing.Instant);
			}
			else if (firing.Instant >= _armedSince && now - firing.Instant <= Freshness)
			{
				Fire(firing, firing.Instant);
			}
		}

		// An event first seen inside its lead window still gets its warning, as long as it has not started.
		void ConsiderStartsSoon(CalendarFiring firing)
		{
			if (firing.Instant > now)
			{
				Schedule(firing.Instant);
			}
			else if (now < firing.Event.Start || now - firing.Instant <= StartsSoonTolerance)
			{
				Fire(firing, firing.Event.Start);
			}
		}

		void Schedule(DateTimeOffset instant) => next = next is null || instant < next ? instant : next;

		void Fire(CalendarFiring firing, DateTimeOffset relevantUntil)
		{
			var key = new FiringKey(firing.Target.Kind, firing.Target.Key, firing.Event.InstanceKey, firing.Instant);
			if (_fired.TryAdd(key, relevantUntil))
			{
				due.Add(firing);
			}
		}
	}

	private readonly record struct FiringKey(
		CalendarFiringKind Kind,
		object Target,
		string InstanceKey,
		DateTimeOffset Instant);
}
