using System.Text.Json;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Widgets;

namespace MacroDeckHost.Application.Timers;

public enum TimerWidgetKind
{
	Countdown,
	Stopwatch,
}

public enum TimerWidgetPhase
{
	Idle,
	Running,
	Paused,
	Finished,
}

public enum TimerGesture
{
	Press,
	LongPress,
}

public sealed record TimerWidgetConfig(TimerWidgetKind Kind, bool AsksForDuration, int DurationSeconds)
{
	public const int DefaultDurationSeconds = 300;

	public const int MinDurationSeconds = 1;

	public const int MaxDurationSeconds = 86_399;

	public const string AskMode = "ask";

	public static bool IsTimerType(string? widgetType)
		=> widgetType is WidgetTypeIds.Countdown or WidgetTypeIds.Stopwatch;

	public static TimerWidgetConfig? FromWidget(WidgetEntity widget)
	{
		ArgumentNullException.ThrowIfNull(widget);

		return widget.Type switch
		{
			WidgetTypeIds.Countdown => ParseCountdown(widget.Data),
			WidgetTypeIds.Stopwatch => new TimerWidgetConfig(TimerWidgetKind.Stopwatch, false, 0),
			_ => null,
		};
	}

	public static int ClampSeconds(double seconds)
		=> double.IsFinite(seconds)
			? (int)Math.Clamp(Math.Round(seconds, MidpointRounding.AwayFromZero), MinDurationSeconds, MaxDurationSeconds)
			: DefaultDurationSeconds;

	private static TimerWidgetConfig ParseCountdown(string? data)
	{
		var asks = false;
		var seconds = DefaultDurationSeconds;

		if (!string.IsNullOrWhiteSpace(data))
		{
			try
			{
				using var document = JsonDocument.Parse(data);
				var root = document.RootElement;

				if (root.ValueKind == JsonValueKind.Object)
				{
					asks = root.TryGetProperty("mode", out var mode) &&
						mode.ValueKind == JsonValueKind.String &&
						string.Equals(mode.GetString(), AskMode, StringComparison.Ordinal);

					var hours = ReadNumber(root, "durationHours");
					var minutes = ReadNumber(root, "durationMinutes");
					var secondsPart = ReadNumber(root, "durationSeconds");

					if (hours is not null || minutes is not null || secondsPart is not null)
					{
						seconds = ClampSeconds((hours ?? 0) * 3600 + (minutes ?? 0) * 60 + (secondsPart ?? 0));
					}
				}
			}
			catch (JsonException)
			{
			}
		}

		return new TimerWidgetConfig(TimerWidgetKind.Countdown, asks, seconds);
	}

	private static double? ReadNumber(JsonElement data, string name)
		=> data.TryGetProperty(name, out var value) &&
			value.ValueKind == JsonValueKind.Number &&
			value.TryGetDouble(out var number) &&
			double.IsFinite(number)
				? Math.Max(0, number)
				: null;
}
