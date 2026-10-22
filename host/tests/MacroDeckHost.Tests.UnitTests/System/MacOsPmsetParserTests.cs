using MacroDeck.Sdk.Batteries;
using MacroDeckHost.Integrations.System.Providers;

namespace MacroDeckHost.Tests.UnitTests.System;

public class MacOsPmsetParserTests
{
	private const string Header = "Now drawing from 'AC Power'\n";

	private static HostBatteryReading Single(string batteryLine)
	{
		var readings = MacOsPmsetParser.Parse(Header + batteryLine + "\n");
		Assert.That(readings, Has.Count.EqualTo(1));
		return readings![0];
	}

	[Test]
	public void A_laptop_on_power_that_is_not_charging_reads_its_level_and_not_charging()
	{
		var reading = Single(" -InternalBattery-0 (id=23593059)\t80%; AC attached; not charging present: true");

		Assert.That(reading, Is.EqualTo(new HostBatteryReading("0", 80, BatteryChargeState.NotCharging, null, null)));
	}

	[Test]
	public void A_discharging_battery_reports_the_time_until_empty()
	{
		var reading = Single(" -InternalBattery-0 (id=4653155)\t57%; discharging; 3:12 remaining present: true");

		Assert.That(reading,
			Is.EqualTo(new HostBatteryReading("0",
				57,
				BatteryChargeState.Discharging,
				new TimeSpan(3, 12, 0),
				null)));
	}

	[Test]
	public void A_charging_battery_reports_the_time_until_full()
	{
		var reading = Single(" -InternalBattery-0 (id=4653155)\t64%; charging; 1:05 remaining present: true");

		Assert.That(reading,
			Is.EqualTo(new HostBatteryReading("0", 64, BatteryChargeState.Charging, null, new TimeSpan(1, 5, 0))));
	}

	[Test]
	public void A_charged_battery_is_full_without_an_estimate()
	{
		var reading = Single(" -InternalBattery-0 (id=4653155)\t100%; charged; 0:00 remaining present: true");

		Assert.That(reading, Is.EqualTo(new HostBatteryReading("0", 100, BatteryChargeState.Full, null, null)));
	}

	[Test]
	public void Finishing_the_charge_still_counts_as_charging()
	{
		var reading = Single(" -InternalBattery-0 (id=4653155)\t99%; finishing charge; 0:10 remaining present: true");

		Assert.That(reading.ChargeState, Is.EqualTo(BatteryChargeState.Charging));
	}

	[Test]
	public void No_estimate_yet_leaves_the_time_unknown()
	{
		var reading = Single(" -InternalBattery-0 (id=4653155)\t40%; discharging; (no estimate) present: true");

		Assert.That(reading,
			Is.EqualTo(new HostBatteryReading("0", 40, BatteryChargeState.Discharging, null, null)));
	}

	[Test]
	public void A_mac_without_a_battery_has_none()
	{
		Assert.That(MacOsPmsetParser.Parse(Header), Is.Empty);
	}

	[Test]
	public void A_battery_that_is_not_present_is_skipped()
	{
		Assert.That(MacOsPmsetParser.Parse(Header
				+ " -InternalBattery-0 (id=4653155)\t0%; AC attached; not charging present: false\n"),
			Is.Empty);
	}

	[Test]
	public void No_output_at_all_is_not_an_answer()
	{
		Assert.That(MacOsPmsetParser.Parse(string.Empty), Is.Null);
	}
}
