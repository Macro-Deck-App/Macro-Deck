using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Locale;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.EmailInbox;

namespace MacroDeckHost.Widgets.EmailInbox;

internal sealed record EmailInboxTime(LocalizedText Text, double Digits)
{
	public static EmailInboxTime None { get; } = new(default, 0);

	public bool IsEmpty => Text.IsEmpty;
}

internal sealed class EmailInboxFormat
{
	private const int MaxShownCount = 999;

	private const double MinDigits = 3;

	private const double DigitSlack = 0.4;

	private const char WideScriptStart = '\u2e80';

	// Yesterday is resolved in each reader's language, so its width is reserved for the longest shipped one.
	private const double YesterdayDigits = 9;

	private readonly TimeOfDayFormat _time;

	public EmailInboxFormat(TimeOfDayFormat time, TimeZoneInfo timeZone)
	{
		ArgumentNullException.ThrowIfNull(time);
		ArgumentNullException.ThrowIfNull(timeZone);

		_time = time;
		TimeZone = timeZone;
	}

	public TimeZoneInfo TimeZone { get; }

	private CultureInfo Culture => _time.Culture;

	public LocalizedText Count(int count)
	{
		return count > MaxShownCount
			? Strings.CountOverflow(MaxShownCount.ToString(Culture))
			: count.ToString(Culture);
	}

	public EmailInboxTime Received(DateTimeOffset received, DateTimeOffset now)
	{
		var local = TimeZoneInfo.ConvertTime(received, TimeZone);
		var day = DateOnly.FromDateTime(local.DateTime);
		var today = LocalDate(now);

		if (day >= today)
		{
			return Formatted(_time.Format(TimeOnly.FromDateTime(local.DateTime)));
		}

		if (day == today.AddDays(-1))
		{
			return new EmailInboxTime(Strings.Yesterday(), YesterdayDigits);
		}

		if (day > today.AddDays(-7))
		{
			return Formatted(day.ToString("ddd", Culture));
		}

		return Formatted(day.Year == today.Year
			? day.ToString(Culture.DateTimeFormat.MonthDayPattern.Replace("MMMM", "MMM", StringComparison.Ordinal),
				Culture)
			: day.ToString(Culture.DateTimeFormat.ShortDatePattern.Replace("yyyy", "yy", StringComparison.Ordinal),
				Culture));
	}

	private static EmailInboxTime Formatted(string text)
	{
		return new EmailInboxTime(text, Math.Max(MinDigits, DigitWidths(text)));
	}

	// A digit-width estimate of the formatted text, so the time can reserve its room and never ellipsize.
	private static double DigitWidths(string text)
	{
		return DigitSlack
			+ text.Sum(character => character switch
			{
				_ when char.IsDigit(character) => 1,
				_ when char.IsWhiteSpace(character) || char.IsPunctuation(character) => 0.5,
				>= WideScriptStart => 1.9,
				_ when char.IsUpper(character) => 1.3,
				_ => 1.05
			});
	}

	public DateTimeOffset NextMidnight(DateTimeOffset now)
	{
		var tomorrow = LocalDate(now).AddDays(1).ToDateTime(TimeOnly.MinValue);
		var offset = TimeZone.GetUtcOffset(tomorrow);
		return new DateTimeOffset(tomorrow, offset);
	}

	private DateOnly LocalDate(DateTimeOffset instant)
	{
		return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, TimeZone).DateTime);
	}
}
