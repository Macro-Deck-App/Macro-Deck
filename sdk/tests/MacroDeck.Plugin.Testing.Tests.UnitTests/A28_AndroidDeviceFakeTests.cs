using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Plugin.Testing.Tests.UnitTests.Support;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Android;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

/// <summary>
/// A28 - <see cref="FakeAndroidDeviceManager" /> refuses operations the way a real host does, so plugin logic
/// written against it handles ADB being off or not allowed.
/// </summary>
[TestFixture]
public class A28_AndroidDeviceFakeTests
{
	private static readonly string[] _lifecycleEvents = ["connected:A1", "state:Connecting->Online", "disconnected:A1"];
	private static readonly string[] _connectedAddress = ["192.168.1.20:5555"];

	private static readonly string[] _packageCalls =
	[
		nameof(IAndroidDevice.InstallApkAsync), nameof(IAndroidDevice.IsPackageInstalledAsync),
		nameof(IAndroidDevice.UninstallPackageAsync), nameof(IAndroidDevice.IsPackageInstalledAsync)
	];

	[Test]
	public void Attaching_changing_and_removing_a_device_raises_the_matching_events()
	{
		var manager = new FakeAndroidDeviceManager();
		var seen = new List<string>();
		manager.DeviceConnected += (_, e) => seen.Add($"connected:{e.Device.Serial}");
		manager.DeviceStateChanged += (_, e) => seen.Add($"state:{e.PreviousState}->{e.Device.State}");
		manager.DeviceDisconnected += (_, e) => seen.Add($"disconnected:{e.Device.Serial}");

		manager.AddDevice("A1", AndroidDeviceState.Connecting);
		manager.SetDeviceState("A1", AndroidDeviceState.Online);
		manager.RemoveDevice("A1");

		Assert.Multiple(() =>
		{
			Assert.That(seen, Is.EqualTo(_lifecycleEvents));
			Assert.That(manager.Devices, Is.Empty);
		});
	}

	[TestCase(AndroidDeviceAccess.AdbNotEnabled, AndroidDeviceErrorCode.AdbNotEnabled)]
	[TestCase(AndroidDeviceAccess.AdbNotAllowed, AndroidDeviceErrorCode.AdbNotAllowed)]
	public void Without_access_the_devices_are_hidden_and_every_operation_is_refused(AndroidDeviceAccess access,
		AndroidDeviceErrorCode expected)
	{
		var manager = new FakeAndroidDeviceManager();
		var device = manager.AddDevice("A1");

		manager.SetAccess(access);

		Assert.Multiple(() =>
		{
			Assert.That(manager.Devices, Is.Empty);
			Assert.That(Assert.ThrowsAsync<AndroidDeviceException>(() => device.GetBatteryStateAsync())!.ErrorCode,
				Is.EqualTo(expected));
			Assert.That(Assert.ThrowsAsync<AndroidDeviceException>(() => device.ExecuteShellAsync("true"))!.ErrorCode,
				Is.EqualTo(expected));
		});
	}

	[Test]
	public async Task Install_and_uninstall_are_recorded_and_reflected_by_IsPackageInstalled()
	{
		var device = new FakeAndroidDeviceManager().AddDevice("A1");

		await device.InstallApkAsync(@"C:\builds\com.example.app.apk");
		var afterInstall = await device.IsPackageInstalledAsync("com.example.app");
		await device.UninstallPackageAsync("com.example.app");
		var afterUninstall = await device.IsPackageInstalledAsync("com.example.app");

		Assert.Multiple(() =>
		{
			Assert.That(afterInstall, Is.True);
			Assert.That(afterUninstall, Is.False);
			Assert.That(device.Calls.Select(call => call.Operation), Is.EqualTo(_packageCalls));
		});
	}

	[Test]
	public async Task A_scripted_shell_answer_is_returned_as_given()
	{
		var device = new FakeAndroidDeviceManager().AddDevice("A1");
		device.ShellHandler = command => new AndroidShellResult(command == "getprop ro.product.model" ? 0 : 127, "Pixel 8", string.Empty, false);

		var result = await device.ExecuteShellAsync("getprop ro.product.model");

		Assert.That(result, Is.EqualTo(new AndroidShellResult(0, "Pixel 8", string.Empty, false)));
	}

	[Test]
	public async Task Connecting_attaches_an_online_device_named_after_the_address_and_records_it()
	{
		var android = new FakeAndroidDeviceManager();

		var device = await android.ConnectAsync("192.168.1.20:5555");

		Assert.Multiple(() =>
		{
			Assert.That(android.FindDevice("192.168.1.20:5555"), Is.SameAs(device));
			Assert.That(device.State, Is.EqualTo(AndroidDeviceState.Online));
			Assert.That(android.ConnectedAddresses, Is.EqualTo(_connectedAddress));
		});
	}

	[Test]
	public void A_stable_id_set_later_replaces_only_that_part_of_the_device_info()
	{
		var manager = new FakeAndroidDeviceManager();
		var device = manager.AddDevice("192.168.1.20:5555", info: new AndroidDeviceInfo("Pixel 8", "Google", "shiba"));

		device.StableId = "3fa9c2d1e0b4a756";

		Assert.That(manager.FindDevice("192.168.1.20:5555")!.Info,
			Is.EqualTo(new AndroidDeviceInfo("Pixel 8", "Google", "shiba") { StableId = "3fa9c2d1e0b4a756" }));
	}

	[Test]
	public async Task In_the_harness_the_context_and_dependency_injection_hand_out_the_same_manager()
	{
		await using var harness = PluginTestHarness.Create(builder => builder.RegisterIntegration(_ => new TestIntegration("test.a28")));
		var own = new FakeAndroidDeviceManager();
		await using var withOwn = PluginTestHarness.Create(builder => builder
			.RegisterIntegration(_ => new TestIntegration("test.a28.own"))
			.ConfigureServices((_, services) => services.AddSingleton<IAndroidDeviceManager>(own)));

		Assert.Multiple(() =>
		{
			Assert.That(((IIntegrationContext)harness.Context).AndroidDevices, Is.SameAs(harness.Context.AndroidDevices));
			Assert.That(harness.Services.GetRequiredService<IAndroidDeviceManager>(), Is.SameAs(harness.Context.AndroidDevices));
			Assert.That(harness.Context.AndroidDevices.Access, Is.EqualTo(AndroidDeviceAccess.Unsupported));
			Assert.That(((IIntegrationContext)withOwn.Context).AndroidDevices, Is.SameAs(own));
			Assert.That(withOwn.Services.GetRequiredService<IAndroidDeviceManager>(), Is.SameAs(own));
		});
	}
}
