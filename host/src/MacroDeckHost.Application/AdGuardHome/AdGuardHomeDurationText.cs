using MacroDeck.Localization;
using Durations = MacroDeckHost.Localization.AppStrings.Integrations.AdGuardHome.Durations;

namespace MacroDeckHost.Application.AdGuardHome;

public static class AdGuardHomeDurationText
{
	public static LocalizedText Label(string id)
		=> id switch
		{
			AdGuardHomeWidgetType.IndefiniteDuration => Durations.Indefinitely(),
			_ when AdGuardHomeWidgetType.Duration(id)?.Length is { } length => length.TotalMinutes < 60
				? Durations.Minutes(count: (int)length.TotalMinutes)
				: Durations.Hours(count: (int)length.TotalHours),
			_ => id
		};
}
