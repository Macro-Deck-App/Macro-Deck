using System.Globalization;
using System.Text.Json;
using Cronos;
using MacroDeckHost.Application.Triggers;
using MacroDeck.Sdk.Identity;

namespace MacroDeckHost.Infrastructure.Triggers;

public abstract class TriggerSchedule
{
	public static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(1);

	public abstract DateTimeOffset? NextOccurrence(DateTimeOffset after, TimeZoneInfo timeZone);

	public static TriggerSchedule? TryCreate(EventSubscription subscription)
	{
		if (!QualifiedId.TryParse(subscription.EventId, out var id) ||
			id.OwnerId != TimeEventProvider.ProviderIdValue)
		{
			return null;
		}

		return id.LocalId switch
		{
			TimeEventProvider.IntervalEventId => TryCreateInterval(subscription),
			TimeEventProvider.DailyEventId => TryCreateCron(DailyExpression(subscription)),
			TimeEventProvider.WeeklyEventId => TryCreateCron(WeeklyExpression(subscription)),
			TimeEventProvider.MonthlyEventId => TryCreateCron(MonthlyExpression(subscription)),
			TimeEventProvider.CronEventId => TryCreateCron(ReadString(subscription, "expression")),
			_ => null
		};
	}

	private static IntervalSchedule? TryCreateInterval(EventSubscription subscription)
	{
		var every = ReadNumber(subscription, "every");
		if (every is null or <= 0)
		{
			return null;
		}

		var interval = ReadString(subscription, "unit") switch
		{
			"seconds" => TimeSpan.FromSeconds(every.Value),
			"hours" => TimeSpan.FromHours(every.Value),
			_ => TimeSpan.FromMinutes(every.Value)
		};

		return interval < MinimumInterval ? null : new IntervalSchedule(interval);
	}

	/// <summary>
	/// Builds a schedule from a plain cron expression, for callers that are not event subscriptions. Keeps
	/// one Cronos-backed implementation, and therefore one daylight-saving behaviour, across the host.
	/// </summary>
	public static TriggerSchedule? FromCron(string? expression) => TryCreateCron(expression);

	private static CronSchedule? TryCreateCron(string? expression)
	{
		if (string.IsNullOrWhiteSpace(expression))
		{
			return null;
		}

		try
		{
			return new CronSchedule(CronExpression.Parse(expression, CronFormat.Standard));
		}
		catch (CronFormatException)
		{
			return null;
		}
	}

	private static string? DailyExpression(EventSubscription subscription)
		=> TryReadTimeOfDay(subscription, out var hour, out var minute) ? $"{minute} {hour} * * *" : null;

	private static string? WeeklyExpression(EventSubscription subscription)
	{
		if (!TryReadTimeOfDay(subscription, out var hour, out var minute))
		{
			return null;
		}

		var day = ReadString(subscription, "dayOfWeek");
		return string.IsNullOrWhiteSpace(day) ? null : $"{minute} {hour} * * {day}";
	}

	private static string? MonthlyExpression(EventSubscription subscription)
	{
		if (!TryReadTimeOfDay(subscription, out var hour, out var minute))
		{
			return null;
		}

		var day = ReadNumber(subscription, "dayOfMonth");
		return day is null or < 1 or > 31 ? null : $"{minute} {hour} {(int)day.Value} * *";
	}

	private static bool TryReadTimeOfDay(EventSubscription subscription, out int hour, out int minute)
	{
		hour = 0;
		minute = 0;

		var value = ReadString(subscription, "time");
		if (string.IsNullOrWhiteSpace(value) ||
			!TimeOnly.TryParse(value, CultureInfo.InvariantCulture, out var parsed))
		{
			return false;
		}

		hour = parsed.Hour;
		minute = parsed.Minute;
		return true;
	}

	private static string? ReadString(EventSubscription subscription, string name)
	{
		if (!subscription.Configuration.TryGetValue(name, out var configured))
		{
			return null;
		}

		var element = configured.Value;
		return element.ValueKind switch
		{
			JsonValueKind.String => element.GetString(),
			JsonValueKind.Number => element.ToString(),
			_ => null
		};
	}

	private static double? ReadNumber(EventSubscription subscription, string name)
	{
		if (!subscription.Configuration.TryGetValue(name, out var configured))
		{
			return null;
		}

		var element = configured.Value;
		return element.ValueKind switch
		{
			JsonValueKind.Number => element.GetDouble(),
			JsonValueKind.String when double.TryParse(element.GetString(),
				NumberStyles.Float,
				CultureInfo.InvariantCulture,
				out var parsed) => parsed,
			_ => null
		};
	}

	private sealed class IntervalSchedule : TriggerSchedule
	{
		private readonly TimeSpan _interval;

		public IntervalSchedule(TimeSpan interval)
		{
			_interval = interval;
		}

		public override DateTimeOffset? NextOccurrence(DateTimeOffset after, TimeZoneInfo timeZone)
			=> after + _interval;
	}

	private sealed class CronSchedule : TriggerSchedule
	{
		private readonly CronExpression _expression;

		public CronSchedule(CronExpression expression)
		{
			_expression = expression;
		}

		public override DateTimeOffset? NextOccurrence(DateTimeOffset after, TimeZoneInfo timeZone)
			=> _expression.GetNextOccurrence(after, timeZone);
	}
}
