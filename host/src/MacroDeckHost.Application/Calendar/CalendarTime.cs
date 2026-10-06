namespace MacroDeckHost.Application.Calendar;

public static class CalendarTime
{
	public static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

	public static DateOnly LocalDate(DateTimeOffset instant, TimeZoneInfo timeZone)
		=> DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, timeZone).DateTime);

	public static DateTimeOffset StartOfDay(DateOnly date, TimeZoneInfo timeZone)
	{
		var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

		// Some zones skip midnight on their daylight saving day; the day then starts at the first
		// minute that exists.
		while (timeZone.IsInvalidTime(local))
		{
			local = local.AddMinutes(30);
		}

		return new DateTimeOffset(local, timeZone.GetUtcOffset(local));
	}

	public static (DateTimeOffset From, DateTimeOffset To) SyncWindow(DateTimeOffset now, TimeZoneInfo timeZone)
	{
		var today = LocalDate(now, timeZone);
		return (StartOfDay(today.AddDays(-1), timeZone), StartOfDay(today.AddDays(8), timeZone));
	}

	public static DateTimeOffset NextMidnight(DateTimeOffset now, TimeZoneInfo timeZone)
		=> StartOfDay(LocalDate(now, timeZone).AddDays(1), timeZone);

	public static TimeSpan NextSyncDelay(DateTimeOffset now, TimeZoneInfo timeZone)
	{
		var untilMidnight = NextMidnight(now, timeZone) - now;
		return untilMidnight < PollInterval ? untilMidnight : PollInterval;
	}
}
