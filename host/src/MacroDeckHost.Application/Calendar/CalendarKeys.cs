namespace MacroDeckHost.Application.Calendar;

public static class CalendarKeys
{
	private const char Separator = '/';

	public static string Calendar(string accountId, string calendarId)
	{
		ArgumentNullException.ThrowIfNull(accountId);
		ArgumentNullException.ThrowIfNull(calendarId);

		return Uri.EscapeDataString(accountId) + Separator + Uri.EscapeDataString(calendarId);
	}

	public static bool TryParseCalendar(string? key, out string accountId, out string calendarId)
	{
		accountId = string.Empty;
		calendarId = string.Empty;

		if (string.IsNullOrEmpty(key))
		{
			return false;
		}

		var parts = key.Split(Separator);
		if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
		{
			return false;
		}

		accountId = Uri.UnescapeDataString(parts[0]);
		calendarId = Uri.UnescapeDataString(parts[1]);
		return true;
	}

	public static string EventInstance(string calendarKey, string eventId)
	{
		ArgumentNullException.ThrowIfNull(calendarKey);
		ArgumentNullException.ThrowIfNull(eventId);

		return calendarKey + Separator + Uri.EscapeDataString(eventId);
	}
}
