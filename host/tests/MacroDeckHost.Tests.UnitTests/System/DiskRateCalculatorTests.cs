using MacroDeckHost.Integrations.System.Metrics;

namespace MacroDeckHost.Tests.UnitTests.System;

public class DiskRateCalculatorTests
{
	private static DiskCounters Counters(ulong read, ulong written, double readMs, double writeMs)
		=> new(read, written, TimeSpan.FromMilliseconds(readMs), TimeSpan.FromMilliseconds(writeMs));

	[Test]
	public void Throughput_and_activity_come_from_the_change_over_the_interval()
	{
		var activity = DiskRateCalculator.Calculate(Counters(1000, 0, 0, 0),
			Counters(5000, 2000, 500, 250),
			TimeSpan.FromSeconds(2));

		Assert.Multiple(() =>
		{
			Assert.That(activity!.ReadBytesPerSecond, Is.EqualTo(2000));
			Assert.That(activity.WriteBytesPerSecond, Is.EqualTo(1000));
			Assert.That(activity.ReadActivePercent, Is.EqualTo(25));
			Assert.That(activity.WriteActivePercent, Is.EqualTo(12.5));
		});
	}

	[Test]
	public void The_first_sample_has_nothing_to_compare_with()
		=> Assert.That(DiskRateCalculator.Calculate(null, Counters(10, 10, 1, 1), TimeSpan.FromSeconds(1)), Is.Null);

	[Test]
	public void Counters_that_went_backwards_give_no_rate()
		=> Assert.That(DiskRateCalculator.Calculate(Counters(5000, 10, 1, 1), Counters(10, 10, 1, 1),
			TimeSpan.FromSeconds(1)), Is.Null);

	[Test]
	public void No_time_passing_gives_no_rate()
		=> Assert.That(DiskRateCalculator.Calculate(Counters(0, 0, 0, 0), Counters(10, 10, 1, 1), TimeSpan.Zero),
			Is.Null);

	[Test]
	public void Overlapping_requests_never_report_more_than_full_activity()
	{
		var activity = DiskRateCalculator.Calculate(Counters(0, 0, 0, 0),
			Counters(10, 10, 3000, 1500),
			TimeSpan.FromSeconds(1));

		Assert.Multiple(() =>
		{
			Assert.That(activity!.ReadActivePercent, Is.EqualTo(100));
			Assert.That(activity.WriteActivePercent, Is.EqualTo(100));
		});
	}

	[Test]
	public void A_platform_without_busy_times_still_reports_throughput()
	{
		var activity = DiskRateCalculator.Calculate(new DiskCounters(0, 0, null, null),
			new DiskCounters(4096, 1024, null, null),
			TimeSpan.FromSeconds(1));

		Assert.Multiple(() =>
		{
			Assert.That(activity!.ReadBytesPerSecond, Is.EqualTo(4096));
			Assert.That(activity.ReadActivePercent, Is.Null);
			Assert.That(activity.WriteActivePercent, Is.Null);
		});
	}
}
