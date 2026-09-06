using MacroDeckHost.Application.Services;
using MacroDeckHost.Infrastructure.Backups;

namespace MacroDeckHost.Tests.UnitTests.Backups;

[TestFixture]
public class BackupScheduleCalculatorTests
{
	[Test]
	public void ReportsNoDueTimeWhenSchedulingIsOff()
		=> Assert.That(BackupScheduleCalculator.NextDue(Settings("off"), null, Now(2026, 8, 18, 1, 0)), Is.Null);

	[Test]
	public void SchedulesTheNextDailyRunAfterTheLastOne()
	{
		var lastRun = Now(2026, 8, 18, 3, 0);

		var due = BackupScheduleCalculator.NextDue(Settings("daily"), lastRun, Now(2026, 8, 18, 4, 0));

		Assert.That(due, Is.EqualTo(Local(2026, 8, 19, 3, 0)));
	}

	// A host that was closed over several due times must not come back and take one backup per missed day.
	[Test]
	public void ReportsASingleOverdueRunAfterALongGap()
	{
		var now = Now(2026, 8, 25, 9, 0);

		var due = BackupScheduleCalculator.NextDue(Settings("daily"), Now(2026, 8, 18, 3, 0), now);

		Assert.That(due, Is.EqualTo(now));
	}

	[Test]
	public void IsNotDueBeforeTheConfiguredTimeOfDay()
	{
		var now = Now(2026, 8, 18, 2, 0);

		var due = BackupScheduleCalculator.NextDue(Settings("daily"), Now(2026, 8, 17, 3, 0), now);

		Assert.That(due, Is.GreaterThan(now));
	}

	[Test]
	public void BuildsTheExpressionForEachSupportedFrequency()
		=> Assert.Multiple(() =>
		{
			Assert.That(BackupScheduleCalculator.CronExpression(Settings("daily")), Is.EqualTo("0 3 * * *"));
			Assert.That(BackupScheduleCalculator.CronExpression(Settings("weekly")), Is.EqualTo("0 3 * * 0"));
			Assert.That(BackupScheduleCalculator.CronExpression(Settings("monthly")), Is.EqualTo("0 3 1 * *"));
			Assert.That(BackupScheduleCalculator.CronExpression(Settings("off")), Is.Null);
		});

	// The 31st does not exist in every month; the schedule has to skip those months rather than clamp into
	// a different day, which is what a naive day-of-month calculation does.
	[Test]
	public void SkipsMonthsThatDoNotHaveTheConfiguredDay()
	{
		var settings = Settings("monthly") with { ScheduleDayOfMonth = 31 };

		var due = BackupScheduleCalculator.NextDue(settings, Now(2026, 8, 31, 3, 0), Now(2026, 8, 31, 4, 0));

		Assert.That(due, Is.EqualTo(Local(2026, 10, 31, 3, 0)));
	}

	private static BackupSettings Settings(string frequency)
		=> new(frequency, "03:00", DayOfWeek.Sunday, 1, "keep-latest", 7, true, true);

	private static DateTimeOffset Now(int year, int month, int day, int hour, int minute)
		=> Local(year, month, day, hour, minute);

	private static DateTimeOffset Local(int year, int month, int day, int hour, int minute)
	{
		var unspecified = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);

		return new DateTimeOffset(unspecified, TimeZoneInfo.Local.GetUtcOffset(unspecified));
	}
}
