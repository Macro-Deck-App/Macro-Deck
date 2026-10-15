using MacroDeck.Localization;
using Durations = MacroDeckHost.Localization.AppStrings.Integrations.AdGuardHome.Durations;

namespace MacroDeckHost.Integrations.AdGuardHome.Widgets;

public static class AdGuardHomeDurationText
{
	public static LocalizedText Label(string id)
	{
		return id switch
		{
			AdGuardHomeWidgetType.IndefiniteDuration => Durations.Indefinitely(),
			_ when AdGuardHomeWidgetType.Duration(id)?.Length is { } length => length.TotalMinutes < 60
				? Durations.Minutes((int)length.TotalMinutes)
				: Durations.Hours((int)length.TotalHours),
			_ => id
		};
	}
}
