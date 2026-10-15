using System.Text.RegularExpressions;
using MacroDeck.Sdk.Android;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Integrations.Adb.Connections;
using MacroDeckHost.Integrations.Adb.Variables;

namespace MacroDeckHost.Tests.UnitTests.Adb;

[TestFixture]
internal sealed partial class AdbVariablesTests
{
	private static readonly string[] _expectedNames =
	[
		"adb_connected_device_count",
		"adb_authorized_device_count",
		"adb_device_connected",
		"adb_device_authorized",
		"adb_device_model",
		"adb_device_manufacturer",
		"adb_device_battery_level",
		"adb_device_screen_on",
		"adb_device_locked",
		"adb_device_foreground_app"
	];

	private ScriptedAndroidDevices _devices = null!;
	private AdbDeviceTracker _tracker = null!;

	[SetUp]
	public void SetUp()
	{
		_devices = new ScriptedAndroidDevices();
		_tracker = new AdbDeviceTracker(_devices, TimeProvider.System);
	}

	[TearDown]
	public void TearDown()
	{
		_tracker.Dispose();
	}

	private ScriptedAndroidDevice Attach(
		string serial,
		AndroidDeviceState state = AndroidDeviceState.Online,
		string? model = null,
		string? manufacturer = null)
	{
		var device = _devices.Attach(serial, null, state: state, model: model);
		device.Info = device.Info with { Manufacturer = manufacturer };
		return device;
	}

	private ValueTask<VariableReading> Read(string localId)
	{
		return AdbVariables.ReadAsync(_devices, _tracker, localId, CancellationToken.None);
	}

	[Test]
	public void ProvidedVariables_is_exactly_the_documented_eleven_names()
	{
		Assert.That(AdbVariables.All.Select(variable => variable.Name),
			Is.EquivalentTo(_expectedNames));
	}

	[Test]
	public void ProvidedVariables_is_constant_regardless_of_device_state()
	{
		var beforeNames = AdbVariables.All.Select(variable => variable.Name).ToList();

		_devices.SetAccess(AndroidDeviceAccess.AdbNotEnabled);
		Attach("S1");

		var afterNames = AdbVariables.All.Select(variable => variable.Name).ToList();

		Assert.That(afterNames, Is.EqualTo(beforeNames));
	}

	[Test]
	public void Every_variable_name_matches_the_canonical_pattern()
	{
		Assert.Multiple(() =>
		{
			foreach (var variable in AdbVariables.All)
			{
				Assert.That(CanonicalName().IsMatch(variable.Name!), Is.True, variable.Name);
			}
		});
	}

	[Test]
	public void Every_variable_declares_a_refresh_interval()
	{
		Assert.Multiple(() =>
		{
			foreach (var variable in AdbVariables.All)
			{
				Assert.That(variable.RefreshInterval, Is.Not.Null, variable.Name);
			}
		});
	}

	[Test]
	public async Task Without_android_devices_every_variable_is_unavailable()
	{
		await Assert.MultipleAsync(async () =>
		{
			foreach (var variable in AdbVariables.All)
			{
				Assert.That((await AdbVariables.ReadAsync(null, null, variable.ResolvedId!, CancellationToken.None))
					.Value,
					Is.Null,
					variable.Name);
			}
		});
	}

	[Test]
	public async Task With_adb_turned_off_every_variable_is_unavailable_including_the_counts()
	{
		Attach("S1");
		_devices.SetAccess(AndroidDeviceAccess.AdbNotEnabled);

		await Assert.MultipleAsync(async () =>
		{
			foreach (var variable in AdbVariables.All)
			{
				Assert.That((await Read(variable.ResolvedId!)).Value, Is.Null, variable.Name);
			}
		});
	}

	[Test]
	public async Task ReadAsync_never_runs_a_device_command()
	{
		var device = Attach("S1");
		_devices.DefaultSerial = "S1";
		device.Status = new AndroidDeviceStatus
			{ IsScreenOn = true, IsLocked = false, ForegroundPackage = "com.example.app" };

		foreach (var variable in AdbVariables.All)
		{
			_ = (await Read(variable.ResolvedId!)).Value;
		}

		Assert.Multiple(() =>
		{
			Assert.That(_devices.Calls, Has.Count.EqualTo(3));
			Assert.That(_devices.Calls,
				Has.All.Matches<string>(call => call.StartsWith("GetStatus:", StringComparison.Ordinal)));
		});
	}

	[Test]
	public async Task Device_counts_reflect_state_regardless_of_which_device_is_default()
	{
		Attach("S1", AndroidDeviceState.Online);
		Attach("S2", AndroidDeviceState.Unauthorized);
		Attach("S3", AndroidDeviceState.Online);
		_devices.Detach("S3");

		await Assert.MultipleAsync(async () =>
		{
			Assert.That((await Read(AdbVariables.ConnectedDeviceCount)).Value, Is.EqualTo(2));
			Assert.That((await Read(AdbVariables.AuthorizedDeviceCount)).Value, Is.EqualTo(1));
		});
	}

	[Test]
	public void No_provided_variable_exposes_a_device_serial()
	{
		Assert.That(AdbVariables.All.Select(variable => variable.Name),
			Has.None.Contains("serial"),
			"a serial must not be readable through a variable");
	}

	[Test]
	public async Task Device_fields_resolve_the_explicitly_configured_default_device()
	{
		Attach("S1", AndroidDeviceState.Online, "Pixel 8", "Google");
		Attach("S2", AndroidDeviceState.Online, "Galaxy S24", "Samsung");
		_devices.DefaultSerial = "S2";

		await Assert.MultipleAsync(async () =>
		{
			Assert.That((await Read(AdbVariables.DeviceModel)).Value, Is.EqualTo("Galaxy S24"));
			Assert.That((await Read(AdbVariables.DeviceManufacturer)).Value, Is.EqualTo("Samsung"));
		});
	}

	[Test]
	public async Task Device_fields_fall_back_to_the_single_connected_device_when_nothing_is_configured()
	{
		Attach("S1", AndroidDeviceState.Online, "Pixel 8");

		await Assert.MultipleAsync(async () =>
		{
			Assert.That((await Read(AdbVariables.DeviceConnected)).Value, Is.True);
			Assert.That((await Read(AdbVariables.DeviceModel)).Value, Is.EqualTo("Pixel 8"));
		});
	}

	[Test]
	public async Task Device_fields_are_unavailable_when_several_devices_are_connected_and_none_is_configured()
	{
		Attach("S1");
		Attach("S2");

		await Assert.MultipleAsync(async () =>
		{
			Assert.That((await Read(AdbVariables.DeviceConnected)).Value, Is.Null);
			Assert.That((await Read(AdbVariables.DeviceModel)).Value, Is.Null);
		});
	}

	[Test]
	public async Task A_configured_but_now_disconnected_device_reads_connected_as_false_not_unavailable()
	{
		Attach("S1", model: "Pixel 8");
		_devices.Detach("S1");
		_devices.DefaultSerial = "S1";

		await Assert.MultipleAsync(async () =>
		{
			Assert.That((await Read(AdbVariables.DeviceConnected)).Value, Is.False);
			Assert.That((await Read(AdbVariables.DeviceAuthorized)).Value, Is.False);
			Assert.That((await Read(AdbVariables.DeviceModel)).Value, Is.EqualTo("Pixel 8"));
		});
	}

	[Test]
	public async Task Status_backed_fields_read_the_default_devices_status_and_battery()
	{
		var device = Attach("S1");
		device.Battery = device.Battery with { Level = 42 };
		device.Status = new AndroidDeviceStatus
			{ IsScreenOn = true, IsLocked = false, ForegroundPackage = "com.example.app" };

		await Assert.MultipleAsync(async () =>
		{
			Assert.That((await Read(AdbVariables.DeviceBatteryLevel)).Value, Is.EqualTo(42));
			Assert.That((await Read(AdbVariables.DeviceScreenOn)).Value, Is.True);
			Assert.That((await Read(AdbVariables.DeviceLocked)).Value, Is.False);
			Assert.That((await Read(AdbVariables.DeviceForegroundApp)).Value, Is.EqualTo("com.example.app"));
		});
	}

	[Test]
	public async Task Status_backed_fields_are_unavailable_while_the_host_has_no_reading_yet()
	{
		Attach("S1").Status = null;

		Assert.That(await Read(AdbVariables.DeviceScreenOn), Is.EqualTo(VariableReading.Unavailable));
	}

	[Test]
	public async Task The_battery_level_is_unavailable_when_the_device_cannot_report_it()
	{
		Attach("S1").BatteryFailure = new AndroidDeviceException(AndroidDeviceErrorCode.Unsupported, "no battery");

		Assert.That(await Read(AdbVariables.DeviceBatteryLevel), Is.EqualTo(VariableReading.Unavailable));
	}

	[Test]
	public async Task Status_backed_fields_are_unavailable_without_a_default_device()
	{
		Attach("S1");
		Attach("S2");

		Assert.That(await Read(AdbVariables.DeviceForegroundApp), Is.EqualTo(VariableReading.Unavailable));
	}

	[Test]
	public async Task An_unknown_variable_name_is_unavailable()
	{
		Attach("S1");

		Assert.That((await Read("not_a_real_variable")).Value, Is.Null);
	}

	[TestCase("ABC123", "abc123")]
	[TestCase("emulator-5554", "emulator_5554")]
	[TestCase("192.168.1.5:5555", "192_168_1_5_5555")]
	[TestCase("R58--M20", "r58_m20")]
	public void SanitizeSerial_produces_the_canonical_shape(string serial, string expected)
	{
		Assert.That(AdbVariables.SanitizeSerial(serial), Is.EqualTo(expected));
	}

	[Test]
	public void DeviceTemplates_names_carry_the_device_placeholder()
	{
		Assert.Multiple(() =>
		{
			foreach (var template in AdbVariables.DeviceTemplates)
			{
				Assert.That(VariableNameTemplate.IsTemplate(template.Name), Is.True, template.Name);
				Assert.That(template.Name, Does.StartWith("adb_device_"), template.Name);
			}
		});
	}

	[Test]
	public void DeviceTemplates_covers_all_four_tier_two_fields()
	{
		var suffixes = AdbVariables.DeviceTemplates.Select(template => template.Name!.Split('_')[^1]).ToList();

		Assert.That(suffixes, Is.EquivalentTo(new List<string> { "connected", "authorized", "model", "level" }));
	}

	[GeneratedRegex("^[a-z][a-z0-9_]*$")]
	private static partial Regex CanonicalName();
}
