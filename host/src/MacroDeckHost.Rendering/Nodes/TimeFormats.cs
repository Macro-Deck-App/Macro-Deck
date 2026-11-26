using System.Globalization;
using System.Text.Json;
using MacroDeckHost.Rendering.Tree;

namespace MacroDeckHost.Rendering.Nodes;

internal sealed record TimeRun(string Before, string Seconds, string After);

internal static class TimeFormats
{
	public static string? Zone(UiNode node, string key)
	{
		if (node.Raw(key) is not { ValueKind: JsonValueKind.Object } raw
			|| !raw.TryGetProperty("$time", out var marker)
			|| marker.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		return marker.TryGetProperty("zone", out var zone) && zone.ValueKind == JsonValueKind.String
			? zone.GetString() ?? ""
			: "";
	}

	public static bool HasReference(UiNode node, string key)
	{
		return Zone(node, key) is not null;
	}

	public static long? TickPeriodMs(UiNode node)
	{
		if (!HasReference(node, "value"))
		{
			return null;
		}

		var format = node.String("format") ?? "";
		if (format is "time" or "time-12h" or "time-12h-padded" or "time-24h" or "time-24h-unpadded")
		{
			return node.Boolean("seconds") == true ? 1000 : 60_000;
		}

		return format is "date" or "date-long" or "date-day-first" or "date-month-first" or "date-iso" or "zone-offset"
			? 60_000
			: null;
	}

	public static DateTime Local(DateTimeOffset instant, string? zone, TimeZoneInfo fallback)
	{
		var timeZone = Resolve(zone) ?? fallback;
		return TimeZoneInfo.ConvertTime(instant, timeZone).DateTime;
	}

	private static TimeZoneInfo? Resolve(string? zone)
	{
		if (string.IsNullOrEmpty(zone))
		{
			return null;
		}

		try
		{
			return TimeZoneInfo.FindSystemTimeZoneById(zone);
		}
		catch (TimeZoneNotFoundException)
		{
			return null;
		}
		catch (InvalidTimeZoneException)
		{
			return null;
		}
	}

	public static TimeRun Run(UiNode node, RenderHost host)
	{
		var zone = Zone(node, "value");
		if (zone is null)
		{
			return new TimeRun("", "", "");
		}

		var local = Local(host.Now, zone, host.TimeZone);
		var culture = CultureOf(host.Culture);
		var seconds = node.Boolean("seconds") == true;

		TimeRun Plain(string text)
		{
			return new TimeRun(text, "", "");
		}

		return (node.String("format") ?? "") switch
		{
			"time" => Time(local, seconds, culture, null),
			"time-12h" => Time(local, seconds, culture, (true, false)),
			"time-12h-padded" => Time(local, seconds, culture, (true, true)),
			"time-24h" => Time(local, seconds, culture, (false, true)),
			"time-24h-unpadded" => Time(local, seconds, culture, (false, false)),
			"date-day-first" => Plain($"{local.Day:00}/{local.Month:00}/{local.Year % 100:00}"),
			"date-month-first" => Plain($"{local.Month:00}/{local.Day:00}/{local.Year % 100:00}"),
			"date-iso" => Plain($"{local.Year:0000}-{local.Month:00}-{local.Day:00}"),
			"date" => Plain(ShortDate(local, culture)),
			"date-long" => Plain(LongDate(local, culture)),
			"zone-name" => Plain(Resolve(zone) is null ? "" : zone.Split('/')[^1].Replace('_', ' ')),
			"zone-offset" => Plain(Resolve(zone) is { } timeZone ? Offset(timeZone.GetUtcOffset(host.Now)) : ""),
			_ => new TimeRun("", "", "")
		};
	}

	public static CultureInfo CultureOf(string name)
	{
		try
		{
			return CultureInfo.GetCultureInfo(name);
		}
		catch (CultureNotFoundException)
		{
			return CultureInfo.GetCultureInfo("en-US");
		}
	}

	private static bool UsesTwelveHours(CultureInfo culture)
	{
		return culture.DateTimeFormat.ShortTimePattern.Contains('h', StringComparison.Ordinal);
	}

	private static TimeRun Time(
		DateTime local,
		bool withSeconds,
		CultureInfo culture,
		(bool Twelve, bool Padded)? pinned)
	{
		var twelve = pinned?.Twelve ?? UsesTwelveHours(culture);
		var hourValue = twelve ? local.Hour % 12 == 0 ? 12 : local.Hour % 12 : local.Hour;
		var padded = pinned?.Padded
			?? !twelve && culture.DateTimeFormat.ShortTimePattern.Contains("HH", StringComparison.Ordinal);
		var hour = padded
			? hourValue.ToString("00", CultureInfo.InvariantCulture)
			: hourValue.ToString(CultureInfo.InvariantCulture);
		var before = $"{hour}:{local.Minute:00}";
		var period = twelve ? " " + (local.Hour < 12 ? AmDesignator(culture) : PmDesignator(culture)) : "";
		return withSeconds ? new TimeRun(before, $":{local.Second:00}", period) : new TimeRun(before + period, "", "");
	}

	private static string AmDesignator(CultureInfo culture)
	{
		return culture.DateTimeFormat.AMDesignator is { Length: > 0 } designator ? designator : "AM";
	}

	private static string PmDesignator(CultureInfo culture)
	{
		return culture.DateTimeFormat.PMDesignator is { Length: > 0 } designator ? designator : "PM";
	}

	private static string ShortDate(DateTime local, CultureInfo culture)
	{
		var format = culture.DateTimeFormat;
		var weekday = format.AbbreviatedDayNames[(int)local.DayOfWeek].TrimEnd('.');
		var month = format.AbbreviatedMonthNames[local.Month - 1].TrimEnd('.');
		return culture.Name == "en-US" || culture.TwoLetterISOLanguageName == "en" && culture.Name.Length == 2
			? $"{weekday}, {month} {local.Day}"
			: $"{weekday}, {local.Day}. {month}";
	}

	private static string LongDate(DateTime local, CultureInfo culture)
	{
		var format = culture.DateTimeFormat;
		var weekday = format.DayNames[(int)local.DayOfWeek];
		var month = format.MonthNames[local.Month - 1];
		return culture.TwoLetterISOLanguageName == "en"
			? $"{weekday}, {month} {local.Day}"
			: $"{weekday}, {local.Day}. {month}";
	}

	private static string Offset(TimeSpan offset)
	{
		var minutes = (int)Math.Round(offset.TotalMinutes);
		var sign = minutes < 0 ? "-" : "+";
		var magnitude = Math.Abs(minutes);
		return $"UTC{sign}{magnitude / 60:00}:{magnitude % 60:00}";
	}
}
