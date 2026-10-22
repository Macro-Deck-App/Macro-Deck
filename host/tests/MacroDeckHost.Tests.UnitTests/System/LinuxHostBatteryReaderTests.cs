using MacroDeck.Sdk.Batteries;
using MacroDeckHost.Integrations.System.Providers;

namespace MacroDeckHost.Tests.UnitTests.System;

public class LinuxHostBatteryReaderTests
{
	private string _root = string.Empty;

	[SetUp]
	public void SetUp()
	{
		_root = Directory.CreateTempSubdirectory("macrodeck-power-supply").FullName;
	}

	[TearDown]
	public void TearDown()
	{
		Directory.Delete(_root, true);
	}

	private void Supply(string name, params (string Attribute, string Value)[] attributes)
	{
		var directory = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
		foreach (var (attribute, value) in attributes)
		{
			File.WriteAllText(Path.Combine(directory, attribute), value + "\n");
		}
	}

	private Task<IReadOnlyList<HostBatteryReading>?> ReadAsync()
	{
		return new LinuxHostBatteryReader(_root).ReadAsync(CancellationToken.None);
	}

	[Test]
	public async Task A_present_system_battery_is_read_with_its_level_status_and_estimate()
	{
		Supply("BAT0",
			("type", "Battery"),
			("scope", "System"),
			("present", "1"),
			("capacity", "73"),
			("status", "Discharging"),
			("time_to_empty_now", "7200"));

		var readings = await ReadAsync();

		Assert.That(readings,
			Is.EqualTo(new[]
			{
				new HostBatteryReading("BAT0", 73, BatteryChargeState.Discharging, TimeSpan.FromHours(2), null)
			}));
	}

	[Test]
	public async Task Absent_batteries_peripherals_and_mains_are_not_the_computers_battery()
	{
		Supply("BAT0", ("type", "Battery"), ("present", "1"), ("capacity", "50"), ("status", "Charging"));
		Supply("BAT1", ("type", "Battery"), ("present", "0"), ("capacity", "0"), ("status", "Unknown"));
		Supply("hidpp_battery_0", ("type", "Battery"), ("scope", "Device"), ("capacity", "90"));
		Supply("AC", ("type", "Mains"), ("online", "1"));

		var readings = await ReadAsync();

		Assert.That(readings!.Select(reading => reading.Key), Is.EqualTo(new[] { "BAT0" }));
	}

	[TestCase("Charging", BatteryChargeState.Charging)]
	[TestCase("Discharging", BatteryChargeState.Discharging)]
	[TestCase("Full", BatteryChargeState.Full)]
	[TestCase("Not charging", BatteryChargeState.NotCharging)]
	[TestCase("Unknown", BatteryChargeState.Unknown)]
	public async Task The_kernel_status_maps_to_the_charge_state(string status, BatteryChargeState expected)
	{
		Supply("BAT0", ("type", "Battery"), ("capacity", "60"), ("status", status));

		var readings = await ReadAsync();

		Assert.That(readings!.Single().ChargeState, Is.EqualTo(expected));
	}

	[Test]
	public async Task A_charging_battery_reports_the_time_until_full()
	{
		Supply("BAT0", ("type", "Battery"), ("capacity", "40"), ("status", "Charging"), ("time_to_full_now", "1800"));

		var readings = await ReadAsync();

		Assert.That(readings!.Single().TimeToFull, Is.EqualTo(TimeSpan.FromMinutes(30)));
	}

	[Test]
	public async Task A_computer_without_a_power_supply_class_has_no_battery()
	{
		var readings = await new LinuxHostBatteryReader(Path.Combine(_root, "missing"))
			.ReadAsync(CancellationToken.None);

		Assert.That(readings, Is.Empty);
	}
}
