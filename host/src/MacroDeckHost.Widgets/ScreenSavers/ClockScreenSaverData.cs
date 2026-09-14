using System.Text.Json;
using MacroDeckHost.Widgets.Clock;
using MacroDeckHost.Widgets.Configuration;

namespace MacroDeckHost.Widgets.ScreenSavers;

public sealed record ClockScreenSaverData
{
	public string HourCycle { get; init; } = "auto";

	public bool ShowDate { get; init; } = true;

	public bool ShowSeconds { get; init; }

	public static ClockScreenSaverData Parse(JsonElement data)
		=> new()
		{
			HourCycle = WidgetConfigJson.ReadString(data, "hourCycle") ?? "auto",
			ShowDate = WidgetConfigJson.ReadBool(data, "showDate") ?? true,
			ShowSeconds = WidgetConfigJson.ReadBool(data, "showSeconds") ?? false,
		};

	public ClockWidgetData ToClock()
		=> new()
		{
			ShowDate = ShowDate,
			ShowSeconds = ShowSeconds,
			HourCycle = HourCycle switch
			{
				"12h" => ClockHourCycle.TwelveHour,
				"24h" => ClockHourCycle.TwentyFourHour,
				_ => ClockHourCycle.Auto,
			},
		};
}
