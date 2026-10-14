using MacroDeckHost.Application.Adb;
using MacroDeckHost.Infrastructure.Adb;

namespace MacroDeckHost.Tests.UnitTests.Adb;

[TestFixture]
public class AdbManagerStableIdTests
{
	private const string HardwareSerial = "R5CT20ABCDE";
	private const string UsbSerial = HardwareSerial;
	private const string WirelessSerial = "192.168.1.20:5555";

	private static AdbManagerHarness Harness()
	{
		var harness = new AdbManagerHarness();
		harness.Runner.When(argv => argv.Contains("getprop ro.serialno"),
			new AdbProcessResult(true, 0, $"{HardwareSerial}\n", string.Empty, false));
		return harness;
	}

	private static void Attached(AdbManagerHarness harness, string serial)
	{
		harness.Runner.When(argv => argv is ["devices", "-l"],
			_ => new AdbProcessResult(true,
				0,
				$"List of devices attached\n{serial} device product:a54x model:SM_A546B transport_id:1\n",
				string.Empty,
				false));
	}

	private static async Task<string?> StableIdSeenAs(string serial, byte[]? installKey = null)
	{
		using var harness = Harness();
		if (installKey is not null)
		{
			harness.PreferenceService.DeviceIdKey = installKey;
		}

		Attached(harness, serial);
		await harness.Manager.RefreshNowAsync(CancellationToken.None);
		return harness.Manager.Devices.Single().StableId;
	}

	[Test]
	public async Task The_same_phone_keeps_its_id_over_usb_and_wifi_and_the_id_never_contains_its_serial()
	{
		var overUsb = await StableIdSeenAs(UsbSerial);
		var overWifi = await StableIdSeenAs(WirelessSerial);

		Assert.Multiple(() =>
		{
			Assert.That(overUsb, Does.Match("^[0-9a-f]{16}$"));
			Assert.That(overWifi, Is.EqualTo(overUsb));
			Assert.That(overUsb, Does.Not.Contain(HardwareSerial.ToLowerInvariant()));
		});
	}

	[Test]
	public async Task Another_installation_derives_a_different_id_for_the_same_phone()
	{
		var here = await StableIdSeenAs(UsbSerial);
		var elsewhere = await StableIdSeenAs(UsbSerial, [.. Enumerable.Repeat((byte)7, 32)]);

		Assert.That(elsewhere, Is.Not.EqualTo(here));
	}

	[Test]
	public async Task A_device_without_a_hardware_serial_falls_back_to_its_adb_serial()
	{
		using var harness = new AdbManagerHarness();
		harness.Runner.When(argv => argv.Contains("getprop ro.serialno"),
			new AdbProcessResult(true, 0, "\n", string.Empty, false));
		Attached(harness, WirelessSerial);

		await harness.Manager.RefreshNowAsync(CancellationToken.None);

		Assert.That(harness.Manager.Devices.Single().StableId,
			Is.EqualTo(AdbStableId.Compute(harness.PreferenceService.DeviceIdKey, null, WirelessSerial)));
	}

	[Test]
	public async Task The_identity_is_probed_once_per_connection()
	{
		using var harness = Harness();
		var attached = true;
		harness.Runner.When(argv => argv is ["devices", "-l"],
			_ => new AdbProcessResult(true,
				0,
				attached
					? $"List of devices attached\n{UsbSerial} device product:a54x model:SM_A546B transport_id:1\n"
					: "List of devices attached\n",
				string.Empty,
				false));

		await harness.Manager.RefreshNowAsync(CancellationToken.None);
		await harness.Manager.RefreshNowAsync(CancellationToken.None);
		attached = false;
		await harness.Manager.RefreshNowAsync(CancellationToken.None);
		attached = true;
		await harness.Manager.RefreshNowAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(harness.Runner.Invocations.Count(argv => argv.Contains("getprop ro.serialno")), Is.EqualTo(2));
			Assert.That(harness.Manager.Devices.Single(device => device.State == AdbDeviceState.Device).StableId,
				Is.Not.Null);
		});
	}

	[Test]
	public async Task An_unanswered_identity_probe_is_retried_on_the_next_refresh()
	{
		using var harness = new AdbManagerHarness();
		var answered = false;
		harness.Runner.When(argv => argv.Contains("getprop ro.serialno"),
			_ => answered
				? new AdbProcessResult(true, 0, $"{HardwareSerial}\n", string.Empty, false)
				: new AdbProcessResult(true, -1, string.Empty, string.Empty, true));
		Attached(harness, UsbSerial);

		await harness.Manager.RefreshNowAsync(CancellationToken.None);
		var whileUnanswered = harness.Manager.Devices.Single().StableId;
		answered = true;
		await harness.Manager.RefreshNowAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(whileUnanswered, Is.Null);
			Assert.That(harness.Manager.Devices.Single().StableId, Does.Match("^[0-9a-f]{16}$"));
		});
	}
}
