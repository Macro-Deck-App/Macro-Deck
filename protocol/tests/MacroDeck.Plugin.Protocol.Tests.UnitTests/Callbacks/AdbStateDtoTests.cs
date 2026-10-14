using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Callbacks;

[TestFixture]
public class AdbStateDtoTests
{
	private const string PushWithStableId = """
											{"access":"available","revision":3,"devices":[
												{"serial":"192.168.1.20:5555","state":"online","model":"Pixel 8","stableId":"3fa9c2d1e0b4a756"}]}
											""";

	[Test]
	public void A_reader_built_before_the_stable_id_existed_reads_a_push_that_carries_one()
	{
		var state = JsonSerializer.Deserialize<AdbStateBeforeStableId>(PushWithStableId, PluginProtocolJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(state.Access, Is.EqualTo(AdbAccessStates.Available));
			Assert.That(state.Revision, Is.EqualTo(3));
			Assert.That(state.Devices.Single(),
				Is.EqualTo(new AdbDeviceBeforeStableId
				{
					Serial = "192.168.1.20:5555", State = AdbDeviceStates.Online, Model = "Pixel 8"
				}));
		});
	}

	[Test]
	public void The_stable_id_is_read_when_present_and_null_from_an_older_host()
	{
		var current = JsonSerializer.Deserialize<AdbStateDto>(PushWithStableId, PluginProtocolJson.Options)!;
		var older = JsonSerializer.Deserialize<AdbStateDto>(
			"""{"access":"available","revision":1,"devices":[{"serial":"A1","state":"online"}]}""",
			PluginProtocolJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(current.Devices.Single().StableId, Is.EqualTo("3fa9c2d1e0b4a756"));
			Assert.That(older.Devices.Single().StableId, Is.Null);
		});
	}

	[Test]
	public void A_reader_built_before_usb_permission_and_the_default_device_reads_such_a_device_as_unauthorized()
	{
		const string push = """
							{"access":"available","revision":4,"defaultDeviceSerial":"A1","adbUnavailable":true,"devices":[
								{"serial":"A1","state":"unauthorized","lacksUsbPermission":true}]}
							""";

		var state = JsonSerializer.Deserialize<AdbStateBeforeStableId>(push, PluginProtocolJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(state.Access, Is.EqualTo(AdbAccessStates.Available));
			Assert.That(state.Devices.Single().State, Is.EqualTo(AdbDeviceStates.Unauthorized));
		});
	}

	[Test]
	public void Usb_permission_and_the_default_device_are_read_when_present_and_absent_from_an_older_host()
	{
		var current = JsonSerializer.Deserialize<AdbStateDto>(
			"""{"access":"available","revision":2,"defaultDeviceSerial":"A1","devices":[{"serial":"A1","state":"unauthorized","lacksUsbPermission":true}]}""",
			PluginProtocolJson.Options)!;
		var older = JsonSerializer.Deserialize<AdbStateDto>(
			"""{"access":"available","revision":1,"devices":[{"serial":"A1","state":"unauthorized"}]}""",
			PluginProtocolJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(current.DefaultDeviceSerial, Is.EqualTo("A1"));
			Assert.That(current.Devices.Single().LacksUsbPermission, Is.True);
			Assert.That(older.DefaultDeviceSerial, Is.Null);
			Assert.That(older.Devices.Single().LacksUsbPermission, Is.Null);
		});
	}

	[Test]
	public void A_reader_built_before_device_modes_reads_a_device_in_recovery_as_offline()
	{
		const string push = """
							{"access":"available","revision":5,"devices":[{"serial":"A1","state":"offline","mode":"recovery"}]}
							""";

		var older = JsonSerializer.Deserialize<AdbStateBeforeStableId>(push, PluginProtocolJson.Options)!;
		var current = JsonSerializer.Deserialize<AdbStateDto>(push, PluginProtocolJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(older.Devices.Single().State, Is.EqualTo(AdbDeviceStates.Offline));
			Assert.That(current.Devices.Single().Mode, Is.EqualTo(AdbDeviceModes.Recovery));
			Assert.That(current.AdbUnavailable, Is.Null);
		});
	}

	[Test]
	public void A_device_with_usb_permission_is_written_without_the_field()
	{
		var json = JsonSerializer.Serialize(new AdbDeviceStateDto { Serial = "A1", State = AdbDeviceStates.Online },
			PluginProtocolJson.Options);

		Assert.That(json, Does.Not.Contain("lacksUsbPermission"));
	}

	private sealed record AdbStateBeforeStableId
	{
		public string Access { get; init; } = AdbAccessStates.NotEnabled;

		public IReadOnlyList<AdbDeviceBeforeStableId> Devices { get; init; } = [];

		public long Revision { get; init; }
	}

	private sealed record AdbDeviceBeforeStableId
	{
		public string Serial { get; init; } = string.Empty;

		public string State { get; init; } = AdbDeviceStates.Offline;

		public string? Model { get; init; }

		public string? Manufacturer { get; init; }

		public string? Product { get; init; }
	}
}
