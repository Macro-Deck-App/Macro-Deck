using MacroDeck.Sdk.Batteries;
using MacroDeckHost.Integrations.System.Providers;

namespace MacroDeckHost.Tests.UnitTests.System;

public class WindowsPowerStatusMapperTests
{
	private const byte AcOffline = 0;
	private const byte AcOnline = 1;
	private const byte AcUnknown = 255;
	private const byte High = 1;
	private const byte Charging = 8;
	private const byte NoSystemBattery = 128;
	private const byte Unknown = 255;
	private const uint NoEstimate = uint.MaxValue;

	private static HostBatteryReading Single(byte ac, byte flag, byte percent, uint lifeTime = NoEstimate)
	{
		var readings = WindowsPowerStatusMapper.Map(new WindowsPowerStatus(ac, flag, percent, lifeTime));
		Assert.That(readings, Has.Count.EqualTo(1));
		return readings![0];
	}

	[TestCase(AcOffline, High, 80, BatteryChargeState.Discharging)]
	[TestCase(AcOnline, High | Charging, 80, BatteryChargeState.Charging)]
	[TestCase(AcOnline, High, 100, BatteryChargeState.Full)]
	[TestCase(AcOnline, High, 80, BatteryChargeState.NotCharging)]
	[TestCase(AcUnknown, High, 80, BatteryChargeState.Unknown)]
	public void The_power_line_and_charging_flag_decide_the_charge_state(
		byte ac,
		byte flag,
		byte percent,
		BatteryChargeState expected)
	{
		var reading = Single(ac, flag, percent);

		Assert.Multiple(() =>
		{
			Assert.That(reading.ChargeState, Is.EqualTo(expected));
			Assert.That(reading.LevelPercent, Is.EqualTo(percent));
		});
	}

	[Test]
	public void A_discharging_battery_reports_its_remaining_time()
	{
		Assert.That(Single(AcOffline, High, 57, 5400).TimeRemaining, Is.EqualTo(TimeSpan.FromMinutes(90)));
	}

	[Test]
	public void An_unknown_remaining_time_stays_unknown()
	{
		Assert.That(Single(AcOffline, High, 57).TimeRemaining, Is.Null);
	}

	[Test]
	public void An_unknown_percentage_leaves_the_level_unknown()
	{
		Assert.That(Single(AcOffline, High, Unknown).LevelPercent, Is.Null);
	}

	[Test]
	public void A_computer_without_a_system_battery_has_none()
	{
		var desktop = new WindowsPowerStatus(AcOnline, NoSystemBattery, Unknown, NoEstimate);

		Assert.That(WindowsPowerStatusMapper.Map(desktop), Is.Empty);
	}

	[Test]
	public void An_unknown_battery_status_or_a_failed_call_is_not_an_answer()
	{
		Assert.Multiple(() =>
		{
			Assert.That(WindowsPowerStatusMapper.Map(new WindowsPowerStatus(AcOnline, Unknown, Unknown, NoEstimate)),
				Is.Null);
			Assert.That(WindowsPowerStatusMapper.Map(null), Is.Null);
		});
	}
}
