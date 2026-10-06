using MacroDeck.Localization;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.Calendar;

namespace MacroDeckHost.Application.Calendar;

public static class CalendarCountdown
{
	public static LocalizedText When(CalendarEventSummary calendarEvent, DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(calendarEvent);

		return calendarEvent.Start <= now ? Strings.NextEvent.Now() : StartsIn(calendarEvent.Start - now);
	}

	public static LocalizedString StartsIn(TimeSpan remaining)
		=> Strings.NextEvent.StartsIn(duration: Countdown(remaining));

	// Whole minutes round up, so an event exactly five minutes away reads "5 minutes" and the text
	// only ever counts down at the instants NextChange names.
	public static LocalizedString Countdown(TimeSpan remaining)
	{
		if (remaining <= TimeSpan.FromMinutes(1))
		{
			return Strings.Countdown.UnderMinute();
		}

		var minutes = WholeMinutes(remaining);
		var days = minutes / (24 * 60);
		var hours = minutes % (24 * 60) / 60;
		var rest = minutes % 60;

		if (days >= 2)
		{
			return Strings.Countdown.Days((int)Math.Round(minutes / (24d * 60), MidpointRounding.AwayFromZero));
		}

		if (days > 0)
		{
			return hours > 0
				? Strings.Countdown.Pair(first: Strings.Countdown.Days((int)days), second: Strings.Countdown.Hours((int)hours))
				: Strings.Countdown.Days((int)days);
		}

		if (hours > 0)
		{
			return rest > 0
				? Strings.Countdown.Pair(first: Strings.Countdown.Hours((int)hours), second: Strings.Countdown.Minutes((int)rest))
				: Strings.Countdown.Hours((int)hours);
		}

		return Strings.Countdown.Minutes((int)rest);
	}

	public static long WholeMinutes(TimeSpan remaining)
		=> remaining <= TimeSpan.Zero ? 0 : (long)Math.Ceiling(remaining.TotalMinutes);

	public static DateTimeOffset NextChange(DateTimeOffset start, DateTimeOffset now)
	{
		var remaining = start - now;

		if (remaining <= TimeSpan.FromMinutes(1))
		{
			return start;
		}

		return start - TimeSpan.FromMinutes(WholeMinutes(remaining) - 1);
	}

	public static LocalizedText Title(string? title)
		=> string.IsNullOrWhiteSpace(title) ? Strings.Untitled() : title;
}
