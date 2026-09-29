using System.Globalization;
using System.Text.Json;
using MacroDeck.Ui.Model.References;
using MacroDeckHost.Application.Timers;
using MacroDeckHost.Widgets.Configuration;

namespace MacroDeckHost.Widgets.Timers;

internal sealed record TimerWidgetSettings(string? Label, bool ShowLabel, string? AccentColor, string? BackgroundColor)
{
	public static TimerWidgetSettings Parse(JsonElement data)
		=> new(Trimmed(WidgetConfigJson.ReadString(data, "label")),
			WidgetConfigJson.ReadBool(data, "showLabel") ?? false,
			Trimmed(WidgetConfigJson.ReadString(data, "accentColor")),
			WidgetColor.NormalizeBackground(WidgetConfigJson.ReadString(data, "backgroundColor")));

	private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed record TimerFace(
	TimerWidgetKind Kind,
	TimerWidgetPhase Phase,
	bool AwaitsDuration,
	double Level,
	UiProgressReference Progress,
	string StaticTime)
{
	// The reader floors whole seconds, so the countdown's length carries 999 ms more than it runs: the
	// readout then rounds up like the countdown_remaining_seconds variable and reaches 0:00 only at the end.
	private const long _roundUpMs = 999;

	public bool IsRunning => Phase == TimerWidgetPhase.Running;

	public bool IsPaused => Phase == TimerWidgetPhase.Paused;

	public bool IsFinished => Phase == TimerWidgetPhase.Finished;

	public static TimerFace From(TimerWidgetSnapshot snapshot, DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(snapshot);

		if (snapshot.Kind == TimerWidgetKind.Countdown)
		{
			var duration = snapshot.DurationMs ?? 0;
			var progress = new UiProgressReference
			{
				PositionMs = snapshot.ElapsedMs,
				Anchor = snapshot.Anchor,
				DurationMs = duration + _roundUpMs,
				Rate = snapshot.IsRunning ? null : 0,
			};

			return new TimerFace(snapshot.Kind,
				snapshot.Phase,
				snapshot.Phase == TimerWidgetPhase.Idle && snapshot.DurationMs is null,
				snapshot.RemainingFractionAt(now),
				progress,
				FormatSeconds(snapshot.RemainingSecondsAt(now)));
		}

		var elapsedSeconds = snapshot.ElapsedSecondsAt(now);

		return new TimerFace(snapshot.Kind,
			snapshot.Phase,
			false,
			snapshot.Phase == TimerWidgetPhase.Idle ? 0 : elapsedSeconds % 60 / 60d,
			new UiProgressReference
			{
				PositionMs = snapshot.ElapsedMs,
				Anchor = snapshot.Anchor,
				Rate = snapshot.IsRunning ? null : 0,
			},
			FormatSeconds(elapsedSeconds));
	}

	public static TimerFace Idle(TimerWidgetConfig config, DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(config);

		return From(new TimerWidgetSnapshot
			{
				WidgetId = Guid.Empty,
				Kind = config.Kind,
				Phase = TimerWidgetPhase.Idle,
				DurationMs = config is { Kind: TimerWidgetKind.Countdown, AsksForDuration: false }
					? config.DurationSeconds * 1000L
					: null,
				Anchor = now,
			},
			now);
	}

	public static string FormatSeconds(int totalSeconds)
	{
		var span = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));

		return span.TotalHours >= 1
			? string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}")
			: string.Create(CultureInfo.InvariantCulture, $"{span.Minutes}:{span.Seconds:00}");
	}
}
