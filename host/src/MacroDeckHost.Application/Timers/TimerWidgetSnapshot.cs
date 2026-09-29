namespace MacroDeckHost.Application.Timers;

public sealed record TimerWidgetSnapshot
{
	public required Guid WidgetId { get; init; }

	public required TimerWidgetKind Kind { get; init; }

	public required TimerWidgetPhase Phase { get; init; }

	public long? DurationMs { get; init; }

	public long ElapsedMs { get; init; }

	public required DateTimeOffset Anchor { get; init; }

	public int? LastEnteredSeconds { get; init; }

	public long Version { get; init; }

	public bool IsRunning => Phase == TimerWidgetPhase.Running;

	public bool IsFinished => Phase == TimerWidgetPhase.Finished;

	public long ElapsedAt(DateTimeOffset now)
	{
		var elapsed = IsRunning ? ElapsedMs + (long)(now - Anchor).TotalMilliseconds : ElapsedMs;
		elapsed = Math.Max(0, elapsed);

		return DurationMs is { } duration ? Math.Min(duration, elapsed) : elapsed;
	}

	public int ElapsedSecondsAt(DateTimeOffset now) => (int)(ElapsedAt(now) / 1000);

	public int RemainingSecondsAt(DateTimeOffset now)
	{
		if (Kind != TimerWidgetKind.Countdown || IsFinished || DurationMs is not { } duration)
		{
			return 0;
		}

		var remaining = duration - ElapsedAt(now);

		return remaining <= 0 ? 0 : (int)((remaining + 999) / 1000);
	}

	public double RemainingFractionAt(DateTimeOffset now)
		=> DurationMs is { } duration && duration > 0
			? Math.Clamp(RemainingSecondsAt(now) * 1000d / duration, 0, 1)
			: 0;

	public bool HasReachedEnd(DateTimeOffset now)
		=> Kind == TimerWidgetKind.Countdown && IsRunning && DurationMs is { } duration && ElapsedAt(now) >= duration;
}

public sealed record TimerWidgetTransition(TimerWidgetSnapshot Snapshot, IReadOnlyList<string> Triggers)
{
	public bool NeedsDuration { get; init; }
}

public sealed class TimerWidgetChangedEventArgs(Guid widgetId) : EventArgs
{
	public Guid WidgetId { get; } = widgetId;
}
