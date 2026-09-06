using MacroDeckHost.Application.Adb;
using MacroDeckHost.Infrastructure.Adb;

namespace MacroDeckHost.Tests.UnitTests.Adb;

public class AdbDeviceListParserTests
{
	private static readonly DateTimeOffset _now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

	private const string FullFixture =
		"List of devices attached\n" +
		"R58M12ABCDE            device product:b0q model:SM_S908B device:b0q transport_id:1\n" +
		"emulator-5554          device product:sdk_gphone64_arm64 model:sdk_gphone64_arm64 " +
		"device:emu64a transport_id:2\n" +
		"1234567890             unauthorized\n" +
		"ABCDEF0123             offline\n" +
		"09876543               no permissions; see [http://developer.android.com/tools/device.html]\n";

	[Test]
	public void Parses_the_full_fixture_into_five_devices_with_correct_states_and_metadata()
	{
		var devices = AdbDeviceListParser.Parse(FullFixture, _now);

		Assert.That(devices, Has.Count.EqualTo(5));
		Assert.Multiple(() =>
		{
			var first = devices[0];
			Assert.That(first.Serial, Is.EqualTo("R58M12ABCDE"));
			Assert.That(first.State, Is.EqualTo(AdbDeviceState.Device));
			Assert.That(first.Model, Is.EqualTo("SM_S908B"));
			Assert.That(first.Product, Is.EqualTo("b0q"));
			Assert.That(first.TransportId, Is.EqualTo("1"));
			Assert.That(first.Manufacturer, Is.Null);
			Assert.That(first.Tunnel, Is.Null);
			Assert.That(first.LastSeenAt, Is.EqualTo(_now));
			Assert.That(first.IsAuthorized, Is.True);

			var second = devices[1];
			Assert.That(second.Serial, Is.EqualTo("emulator-5554"));
			Assert.That(second.State, Is.EqualTo(AdbDeviceState.Device));
			Assert.That(second.Model, Is.EqualTo("sdk_gphone64_arm64"));
			Assert.That(second.Product, Is.EqualTo("sdk_gphone64_arm64"));
			Assert.That(second.TransportId, Is.EqualTo("2"));

			var third = devices[2];
			Assert.That(third.Serial, Is.EqualTo("1234567890"));
			Assert.That(third.State, Is.EqualTo(AdbDeviceState.Unauthorized));
			Assert.That(third.Model, Is.Null);
			Assert.That(third.IsAuthorized, Is.False);

			var fourth = devices[3];
			Assert.That(fourth.Serial, Is.EqualTo("ABCDEF0123"));
			Assert.That(fourth.State, Is.EqualTo(AdbDeviceState.Offline));

			var fifth = devices[4];
			Assert.That(fifth.Serial, Is.EqualTo("09876543"));
			Assert.That(fifth.State, Is.EqualTo(AdbDeviceState.NoPermissions));
			Assert.That(fifth.Model, Is.Null);
		});
	}

	[Test]
	public void Header_only_output_yields_no_devices()
	{
		var devices = AdbDeviceListParser.Parse("List of devices attached\n", _now);

		Assert.That(devices, Is.Empty);
	}

	[Test]
	public void Empty_output_yields_no_devices()
	{
		var devices = AdbDeviceListParser.Parse(string.Empty, _now);

		Assert.That(devices, Is.Empty);
	}

	[Test]
	public void A_no_permissions_line_with_the_trailing_see_url_is_recognized()
	{
		const string output = "List of devices attached\n" +
			"09876543               no permissions; see [http://developer.android.com/tools/device.html]\n";

		var devices = AdbDeviceListParser.Parse(output, _now);

		Assert.That(devices, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(devices[0].Serial, Is.EqualTo("09876543"));
			Assert.That(devices[0].State, Is.EqualTo(AdbDeviceState.NoPermissions));
		});
	}

	[Test]
	public void A_device_line_without_metadata_leaves_the_optional_fields_null()
	{
		const string output = "List of devices attached\n1234567890             unauthorized\n";

		var devices = AdbDeviceListParser.Parse(output, _now);

		Assert.That(devices, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(devices[0].State, Is.EqualTo(AdbDeviceState.Unauthorized));
			Assert.That(devices[0].Model, Is.Null);
			Assert.That(devices[0].Product, Is.Null);
			Assert.That(devices[0].TransportId, Is.Null);
		});
	}

	[Test]
	public void An_unrecognized_state_token_maps_to_Unknown()
	{
		const string output = "List of devices attached\nSERIAL123              weirdstate\n";

		var devices = AdbDeviceListParser.Parse(output, _now);

		Assert.That(devices, Has.Count.EqualTo(1));
		Assert.That(devices[0].State, Is.EqualTo(AdbDeviceState.Unknown));
	}

	[Test]
	public void A_malformed_line_is_skipped_rather_than_throwing()
	{
		const string output = "List of devices attached\n" +
			"   \n" +
			"onlyoneserialword\n" +
			"R58M12ABCDE            device product:b0q model:SM_S908B device:b0q transport_id:1\n";

		IReadOnlyList<AdbDevice> devices = [];

		Assert.DoesNotThrow(() => devices = AdbDeviceListParser.Parse(output, _now));
		Assert.That(devices, Has.Count.EqualTo(1));
		Assert.That(devices[0].Serial, Is.EqualTo("R58M12ABCDE"));
	}

	[Test]
	public void A_jailbroken_car_thing_is_parsed_from_the_line_a_real_device_produces()
	{
		// Ground truth captured from an attached device (issue #727). The `device:evt` token matters:
		// it repeats the state keyword as a metadata key, and it must not be mistaken for either.
		const string line =
			"List of devices attached\n" +
			"123456                 device usb:2-1 product:spotify-car-thing model:Car_Thing device:evt transport_id:1";

		var devices = AdbDeviceListParser.Parse(line, DateTimeOffset.UnixEpoch);

		Assert.That(devices, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(devices[0].Serial, Is.EqualTo("123456"));
			Assert.That(devices[0].State, Is.EqualTo(AdbDeviceState.Device));
			Assert.That(devices[0].Model, Is.EqualTo("Car_Thing"));
			Assert.That(devices[0].Product, Is.EqualTo("spotify-car-thing"));
			Assert.That(devices[0].TransportId, Is.EqualTo("1"));
		});
	}
}
