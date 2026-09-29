using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Previews;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Timers;

namespace MacroDeckHost.Widgets.Timers;

internal static class TimerWidgetViewPreviews
{
	private static readonly DateTimeOffset _anchor = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

	[UiPreview("Countdown running", Profile = UiPreviewProfiles.Widget)]
	public static UiElement CountdownRunning()
		=> Build(Countdown(TimerWidgetPhase.Running, 300_000, 105_000), new TimerWidgetSettings("Pizza", true, null, null));

	[UiPreview("Countdown paused", Profile = UiPreviewProfiles.Widget)]
	public static UiElement CountdownPaused()
		=> Build(Countdown(TimerWidgetPhase.Paused, 300_000, 105_000), new TimerWidgetSettings(null, false, null, null));

	[UiPreview("Countdown waiting for a duration", Profile = UiPreviewProfiles.Widget)]
	public static UiElement CountdownAsking()
		=> Build(Countdown(TimerWidgetPhase.Idle, null, 0), new TimerWidgetSettings(null, false, null, null));

	[UiPreview("Countdown finished", Profile = UiPreviewProfiles.Widget)]
	public static UiElement CountdownFinished()
		=> Build(Countdown(TimerWidgetPhase.Finished, 300_000, 300_000), new TimerWidgetSettings("Pizza", true, null, null));

	[UiPreview("Stopwatch running", Profile = UiPreviewProfiles.Widget)]
	public static UiElement StopwatchRunning()
		=> Build(Stopwatch(TimerWidgetPhase.Running, 83_000), new TimerWidgetSettings(null, false, "#4caf50", null));

	[UiPreview("Stopwatch paused", Profile = UiPreviewProfiles.Widget)]
	public static UiElement StopwatchPaused()
		=> Build(Stopwatch(TimerWidgetPhase.Paused, 3_725_000), new TimerWidgetSettings("Lap", true, null, "#101c2c"));

	private static UiElement Build(TimerWidgetSnapshot snapshot, TimerWidgetSettings settings)
		=> TimerWidgetView.Build(new UiState<TimerFace>(TimerFace.From(snapshot, _anchor)), settings);

	private static TimerWidgetSnapshot Countdown(TimerWidgetPhase phase, long? durationMs, long elapsedMs)
		=> new()
		{
			WidgetId = Guid.Empty,
			Kind = TimerWidgetKind.Countdown,
			Phase = phase,
			DurationMs = durationMs,
			ElapsedMs = elapsedMs,
			Anchor = _anchor,
		};

	private static TimerWidgetSnapshot Stopwatch(TimerWidgetPhase phase, long elapsedMs)
		=> new()
		{
			WidgetId = Guid.Empty,
			Kind = TimerWidgetKind.Stopwatch,
			Phase = phase,
			ElapsedMs = elapsedMs,
			Anchor = _anchor,
		};
}
