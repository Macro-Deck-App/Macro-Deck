using System.Text.Json;
using MacroDeckHost.Widgets.Configuration;

namespace MacroDeckHost.Widgets.Calendar;

internal static class CalendarWidgetSettings
{
	public static string? BackgroundColor(JsonElement data)
		=> WidgetColor.NormalizeBackground(WidgetConfigJson.ReadString(data, "backgroundColor"));
}
