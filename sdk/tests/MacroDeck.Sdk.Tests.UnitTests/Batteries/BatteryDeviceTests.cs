using MacroDeck.Sdk.Batteries;

namespace MacroDeck.Sdk.Tests.UnitTests.Batteries;

[TestFixture]
public class BatteryDeviceTests
{
	[Test]
	public void A_device_with_only_an_id_and_a_name_is_a_connected_device_of_unknown_level_and_kind_other()
	{
		var device = new BatteryDevice { Id = "headset", Name = "Headset" };

		Assert.Multiple(() =>
		{
			Assert.That(device.Kind, Is.EqualTo(BatteryDeviceKinds.Other));
			Assert.That(device.LevelPercent, Is.Null);
			Assert.That(device.IsConnected, Is.True);
			Assert.That(device.ChargeState, Is.EqualTo(BatteryChargeState.Unknown));
			Assert.That(device.Health, Is.EqualTo(BatteryHealth.Unknown));
		});
	}

	[TestCase(0)]
	[TestCase(100)]
	public void Levels_at_the_bounds_are_accepted(int level)
	{
		Assert.That(new BatteryDevice { Id = "a", Name = "A", LevelPercent = level }.LevelPercent, Is.EqualTo(level));
	}

	[TestCase(-1)]
	[TestCase(101)]
	public void A_level_outside_0_to_100_is_refused(int level)
	{
		Assert.That(() => new BatteryDevice { Id = "a", Name = "A", LevelPercent = level },
			Throws.InstanceOf<ArgumentOutOfRangeException>());
	}

	[TestCase("")]
	[TestCase("   ")]
	public void An_empty_id_or_name_is_refused(string value)
	{
		Assert.Multiple(() =>
		{
			Assert.That(() => new BatteryDevice { Id = value, Name = "A" }, Throws.InstanceOf<ArgumentException>());
			Assert.That(() => new BatteryDevice { Id = "a", Name = value }, Throws.InstanceOf<ArgumentException>());
		});
	}

	[Test]
	public void An_id_or_name_beyond_the_limit_is_refused()
	{
		var tooLongId = new string('i', BatteryLimits.MaxIdLength + 1);
		var tooLongName = new string('n', BatteryLimits.MaxNameLength + 1);

		Assert.Multiple(() =>
		{
			Assert.That(() => new BatteryDevice { Id = tooLongId, Name = "A" }, Throws.InstanceOf<ArgumentException>());
			Assert.That(() => new BatteryDevice { Id = "a", Name = tooLongName },
				Throws.InstanceOf<ArgumentException>());
		});
	}

	[Test]
	public void A_negative_time_estimate_is_refused()
	{
		Assert.That(() => new BatteryDevice { Id = "a", Name = "A", TimeRemaining = TimeSpan.FromSeconds(-1) },
			Throws.InstanceOf<ArgumentOutOfRangeException>());
	}

	[Test]
	public void A_kind_this_version_does_not_know_reads_as_other()
	{
		Assert.Multiple(() =>
		{
			Assert.That(BatteryDeviceKinds.Normalize("camera"), Is.EqualTo(BatteryDeviceKinds.Other));
			Assert.That(BatteryDeviceKinds.Normalize(null), Is.EqualTo(BatteryDeviceKinds.Other));
			Assert.That(BatteryDeviceKinds.Normalize(BatteryDeviceKinds.Watch), Is.EqualTo(BatteryDeviceKinds.Watch));
		});
	}
}
