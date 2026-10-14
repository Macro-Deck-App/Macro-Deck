using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Batteries;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Callbacks;

[TestFixture]
public class BatteryWireTests
{
	[Test]
	public void A_published_device_travels_under_its_frozen_wire_names_with_states_as_strings()
	{
		var json = JsonSerializer.SerializeToElement(new BatteriesPublishArguments
			{
				Devices =
				[
					BatteryWire.ToDto(new BatteryDevice
					{
						Id = "pixel",
						Name = "Pixel 9",
						Kind = BatteryDeviceKinds.Phone,
						LevelPercent = 42,
						ChargeState = BatteryChargeState.NotCharging,
						IsConnected = false,
						TimeToFull = TimeSpan.FromMinutes(30),
						Health = BatteryHealth.OverVoltage
					})
				]
			},
			PluginProtocolJson.Options);

		var device = json.GetProperty("devices")[0];
		Assert.Multiple(() =>
		{
			Assert.That(device.GetProperty("id").GetString(), Is.EqualTo("pixel"));
			Assert.That(device.GetProperty("name").GetString(), Is.EqualTo("Pixel 9"));
			Assert.That(device.GetProperty("kind").GetString(), Is.EqualTo("phone"));
			Assert.That(device.GetProperty("levelPercent").GetInt32(), Is.EqualTo(42));
			Assert.That(device.GetProperty("chargeState").GetString(), Is.EqualTo("not-charging"));
			Assert.That(device.GetProperty("isConnected").GetBoolean(), Is.False);
			Assert.That(device.GetProperty("timeToFullSeconds").GetInt64(), Is.EqualTo(1800));
			Assert.That(device.TryGetProperty("timeRemainingSeconds", out _), Is.False);
			Assert.That(device.GetProperty("health").GetString(), Is.EqualTo("over-voltage"));
		});
	}

	[Test]
	public void A_push_from_a_newer_host_reads_values_this_version_does_not_know_as_other_and_unknown()
	{
		var state = JsonSerializer.Deserialize<BatteryStateDto>("""
																{"revision":3,"devices":[{"id":"p:cam","localId":"cam","providerId":"p","name":"Camera",
																"kind":"camera","levelPercent":70,"chargeState":"wireless-charging","isConnected":true,
																"health":"swollen","updatedAt":"2026-10-09T10:00:00+00:00","futureField":1}]}
																""",
			PluginProtocolJson.Options)!;

		var snapshot = BatteryWire.ToSnapshot(state.Devices.Single());

		Assert.Multiple(() =>
		{
			Assert.That(state.RemovedIds, Is.Empty);
			Assert.That(snapshot.Id, Is.EqualTo("p:cam"));
			Assert.That(snapshot.Kind, Is.EqualTo(BatteryDeviceKinds.Other));
			Assert.That(snapshot.ChargeState, Is.EqualTo(BatteryChargeState.Unknown));
			Assert.That(snapshot.Health, Is.EqualTo(BatteryHealth.Unknown));
			Assert.That(snapshot.LevelPercent, Is.EqualTo(70));
			Assert.That(snapshot.UpdatedAt, Is.EqualTo(new DateTimeOffset(2026, 10, 9, 10, 0, 0, TimeSpan.Zero)));
		});
	}

	[Test]
	public void A_published_device_with_an_unknown_charge_state_is_read_as_unknown_not_refused()
	{
		var device = BatteryWire.ToDevice(new BatteryDeviceDto
		{
			Id = "pad", Name = "Pad", Kind = "hoverboard", ChargeState = "trickle", Health = "fine"
		});

		Assert.Multiple(() =>
		{
			Assert.That(device.ChargeState, Is.EqualTo(BatteryChargeState.Unknown));
			Assert.That(device.Health, Is.EqualTo(BatteryHealth.Unknown));
			Assert.That(device.Kind, Is.EqualTo("hoverboard"));
		});
	}

	[TestCase(-1)]
	[TestCase(101)]
	public void A_published_level_outside_0_to_100_is_refused(int level)
	{
		Assert.That(() => BatteryWire.ToDevice(new BatteryDeviceDto { Id = "a", Name = "A", LevelPercent = level }),
			Throws.InstanceOf<ArgumentOutOfRangeException>());
	}

	[Test]
	public void A_published_device_without_a_name_is_refused()
	{
		Assert.That(() => BatteryWire.ToDevice(new BatteryDeviceDto { Id = "a", Name = " " }),
			Throws.InstanceOf<ArgumentException>());
	}

	[Test]
	public void Every_charge_state_and_health_survives_the_round_trip()
	{
		Assert.Multiple(() =>
		{
			foreach (var state in Enum.GetValues<BatteryChargeState>())
			{
				Assert.That(BatteryWire.ToChargeState(BatteryWire.ToWire(state)), Is.EqualTo(state));
			}

			foreach (var health in Enum.GetValues<BatteryHealth>())
			{
				Assert.That(BatteryWire.ToHealth(BatteryWire.ToWire(health)), Is.EqualTo(health));
			}
		});
	}

	[Test]
	public void The_state_push_names_removed_ids_and_the_revision_on_the_wire()
	{
		var json = JsonSerializer.SerializeToElement(new BatteryStateDto { Revision = 5, RemovedIds = ["p:a"] },
			PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(json.GetProperty("revision").GetInt64(), Is.EqualTo(5));
			Assert.That(json.GetProperty("removedIds")[0].GetString(), Is.EqualTo("p:a"));
			Assert.That(json.GetProperty("devices").GetArrayLength(), Is.Zero);
		});
	}
}
