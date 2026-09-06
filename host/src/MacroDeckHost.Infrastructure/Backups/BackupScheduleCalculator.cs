using System.Globalization;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Infrastructure.Triggers;

namespace MacroDeckHost.Infrastructure.Backups;

public static class BackupScheduleCalculator
{
	public static string? CronExpression(BackupSettings settings)
	{
		if (!TryReadTimeOfDay(settings.ScheduleTimeOfDay, out var hour, out var minute))
		{
			return null;
		}

		return settings.ScheduleFrequency switch
		{
			AppPreferenceService.BackupScheduleDaily => $"{minute} {hour} * * *",
			AppPreferenceService.BackupScheduleWeekly => $"{minute} {hour} * * {(int)settings.ScheduleDayOfWeek}",
			AppPreferenceService.BackupScheduleMonthly =>
				$"{minute} {hour} {Math.Clamp(settings.ScheduleDayOfMonth, 1, 31)} * *",
			_ => null
		};
	}

	/// <summary>
	/// The next time a scheduled backup is due. A window missed while the host was closed reports as due
	/// immediately, so a machine that is off overnight still gets one backup rather than one per missed day.
	/// </summary>
	public static DateTimeOffset? NextDue(BackupSettings settings, DateTimeOffset? lastRun, DateTimeOffset now)
	{
		var schedule = TriggerSchedule.FromCron(CronExpression(settings));
		if (schedule is null)
		{
			return null;
		}

		var reference = lastRun ?? now;
		var next = schedule.NextOccurrence(reference, TimeZoneInfo.Local);

		return next is null ? null : next.Value < now ? now : next;
	}

	private static bool TryReadTimeOfDay(string value, out int hour, out int minute)
	{
		hour = 0;
		minute = 0;

		if (!TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
		{
			return false;
		}

		hour = parsed.Hour;
		minute = parsed.Minute;

		return true;
	}
}
