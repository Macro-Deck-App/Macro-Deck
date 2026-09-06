using MacroDeckHost.Application.Adb;
using MacroDeckHost.Infrastructure.Adb;

namespace MacroDeckHost.Tests.UnitTests.Adb;

public class AdbManagerDeviceTests
{
	private const string Serial = "R58M12ABCDE";

	[Test]
	public async Task Diffing_produces_exactly_the_six_change_kinds_with_no_duplicates_on_repeated_identical_ticks()
	{
		using var harness = new AdbManagerHarness();
		var devicesOutput = string.Empty;
		harness.Runner.When(IsDevicesList, _ => new AdbProcessResult(true, 0, devicesOutput, string.Empty, false));

		var changes = new List<AdbDeviceChange>();
		harness.Manager.DeviceChanged += (_, change) => changes.Add(change);

		devicesOutput = DevicesOutput(DeviceLine(Serial, "device"));
		await harness.Manager.RefreshNowAsync(CancellationToken.None); // Connected

		devicesOutput = DevicesOutput(DeviceLine(Serial, "unauthorized"));
		await harness.Manager.RefreshNowAsync(CancellationToken.None); // Unauthorized

		devicesOutput = DevicesOutput(DeviceLine(Serial, "device"));
		await harness.Manager.RefreshNowAsync(CancellationToken.None); // Authorized

		devicesOutput = DevicesOutput(DeviceLine(Serial, "offline"));
		await harness.Manager.RefreshNowAsync(CancellationToken.None); // Offline

		devicesOutput = DevicesOutput();
		await harness.Manager.RefreshNowAsync(CancellationToken.None); // Disconnected

		devicesOutput = DevicesOutput(DeviceLine(Serial, "device"));
		await harness.Manager.RefreshNowAsync(CancellationToken.None); // Online

		await harness.Manager.RefreshNowAsync(CancellationToken.None); // identical repeat: nothing new

		Assert.That(changes.Select(change => change.Kind).ToList(),
			Is.EqualTo(new[]
			{
				AdbDeviceChangeKind.Connected,
				AdbDeviceChangeKind.Unauthorized,
				AdbDeviceChangeKind.Authorized,
				AdbDeviceChangeKind.Offline,
				AdbDeviceChangeKind.Disconnected,
				AdbDeviceChangeKind.Online
			}));
	}

	[Test]
	public async Task A_disappeared_device_is_retained_as_Disconnected_rather_than_vanishing()
	{
		using var harness = new AdbManagerHarness();
		var devicesOutput = DevicesOutput(DeviceLine(Serial, "device"));
		harness.Runner.When(IsDevicesList, _ => new AdbProcessResult(true, 0, devicesOutput, string.Empty, false));

		await harness.Manager.RefreshNowAsync(CancellationToken.None);
		Assert.That(harness.Manager.Devices.Single().State, Is.EqualTo(AdbDeviceState.Device));

		devicesOutput = DevicesOutput();
		await harness.Manager.RefreshNowAsync(CancellationToken.None);

		var device = harness.Manager.Devices.Single();
		Assert.Multiple(() =>
		{
			Assert.That(device.Serial, Is.EqualTo(Serial));
			Assert.That(device.State, Is.EqualTo(AdbDeviceState.Disconnected));
		});
	}

	[Test]
	public async Task A_disconnected_device_is_evicted_once_the_retention_window_elapses()
	{
		using var harness = new AdbManagerHarness();
		var devicesOutput = DevicesOutput(DeviceLine(Serial, "device"));
		harness.Runner.When(IsDevicesList, _ => new AdbProcessResult(true, 0, devicesOutput, string.Empty, false));

		await harness.Manager.RefreshNowAsync(CancellationToken.None);

		devicesOutput = DevicesOutput();
		await harness.Manager.RefreshNowAsync(CancellationToken.None);
		Assert.That(harness.Manager.Devices, Has.Count.EqualTo(1), "still inside the 5 minute retention window");

		harness.TimeProvider.Advance(TimeSpan.FromMinutes(6));
		await harness.Manager.RefreshNowAsync(CancellationToken.None);

		Assert.That(harness.Manager.Devices, Is.Empty);
	}

	[Test]
	public async Task A_throwing_DeviceChanged_subscriber_does_not_propagate_or_stop_other_subscribers()
	{
		using var harness = new AdbManagerHarness();
		harness.Runner.When(IsDevicesList,
			_ => new AdbProcessResult(true, 0, DevicesOutput(DeviceLine(Serial, "device")), string.Empty, false));

		var secondSubscriberCalled = false;
		harness.Manager.DeviceChanged += (_, _) => throw new InvalidOperationException("boom");
		harness.Manager.DeviceChanged += (_, _) => secondSubscriberCalled = true;

		Assert.That(async () => await harness.Manager.RefreshNowAsync(CancellationToken.None), Throws.Nothing);
		Assert.That(secondSubscriberCalled, Is.True);
	}

	[Test]
	public async Task ResolveDevice_treats_null_blank_and_whitespace_the_same_as_the_single_connected_device()
	{
		using var harness = new AdbManagerHarness();
		harness.Runner.When(IsDevicesList,
			_ => new AdbProcessResult(true, 0, DevicesOutput(DeviceLine(Serial, "device")), string.Empty, false));

		await harness.Manager.RefreshNowAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(harness.Manager.ResolveDevice(null)?.Serial, Is.EqualTo(Serial));
			Assert.That(harness.Manager.ResolveDevice(string.Empty)?.Serial, Is.EqualTo(Serial));
			Assert.That(harness.Manager.ResolveDevice("   ")?.Serial, Is.EqualTo(Serial));
			Assert.That(harness.Manager.ResolveDevice("unknown-serial"), Is.Null);
		});
	}

	[Test]
	public async Task ResolveDevice_prefers_the_configured_default_over_the_single_device_fallback()
	{
		using var harness = new AdbManagerHarness();
		const string other = "OTHERSERIAL";
		harness.Runner.When(IsDevicesList,
			_ => new AdbProcessResult(true,
				0,
				DevicesOutput(DeviceLine(Serial, "device"), DeviceLine(other, "device")),
				string.Empty,
				false));
		harness.PreferenceService.AdbSettings
			= harness.PreferenceService.AdbSettings with { DefaultDeviceSerial = other };

		await harness.Manager.ApplySettingsAsync(CancellationToken.None);

		Assert.That(harness.Manager.ResolveDevice(null)?.Serial, Is.EqualTo(other));
	}

	[Test]
	public async Task ResolveDevice_returns_null_when_no_default_is_configured_and_more_than_one_device_is_connected()
	{
		using var harness = new AdbManagerHarness();
		const string other = "OTHERSERIAL";
		harness.Runner.When(IsDevicesList,
			_ => new AdbProcessResult(true,
				0,
				DevicesOutput(DeviceLine(Serial, "device"), DeviceLine(other, "device")),
				string.Empty,
				false));

		await harness.Manager.RefreshNowAsync(CancellationToken.None);

		Assert.That(harness.Manager.ResolveDevice(null), Is.Null);
	}

	[Test]
	public async Task Manufacturer_is_fetched_once_per_serial_and_cached_for_the_process_lifetime()
	{
		using var harness = new AdbManagerHarness();
		harness.Runner.When(IsDevicesList,
			_ => new AdbProcessResult(true, 0, DevicesOutput(DeviceLine(Serial, "device")), string.Empty, false));
		harness.Runner.When(argv => argv.Contains("getprop ro.product.manufacturer"),
			new AdbProcessResult(true, 0, "Google\n", string.Empty, false));

		await harness.Manager.RefreshNowAsync(CancellationToken.None);
		await harness.Manager.RefreshNowAsync(CancellationToken.None);
		await harness.Manager.RefreshNowAsync(CancellationToken.None);

		var manufacturerCalls
			= harness.Runner.Invocations.Count(argv => argv.Contains("getprop ro.product.manufacturer"));
		Assert.Multiple(() =>
		{
			Assert.That(manufacturerCalls, Is.EqualTo(1), "the manufacturer must be fetched only once per serial");
			Assert.That(harness.Manager.Devices.Single().Manufacturer, Is.EqualTo("Google"));
		});
	}

	private static bool IsDevicesList(IReadOnlyList<string> argv) =>
		argv.Count == 2 && argv[0] == "devices" && argv[1] == "-l";

	private static string DeviceLine(string serial, string state, string model = "Pixel", string product = "product")
		=> $"{serial} {state} product:{product} model:{model} transport_id:1";

	private static string DevicesOutput(params string[] lines)
		=> "List of devices attached\n" + string.Join('\n', lines) + "\n";
}
