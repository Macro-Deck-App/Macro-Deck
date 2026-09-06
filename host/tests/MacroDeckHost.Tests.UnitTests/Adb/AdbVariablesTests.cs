using System.Text.RegularExpressions;
using MacroDeckHost.Integrations.Adb;
using MacroDeck.Sdk.Variables;

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

	private FakeAdbGateway _gateway = null!;

	[SetUp]
	public void SetUp()
	{
		_gateway = new FakeAdbGateway();
	}

	private static AdbGatewayDevice Device(
		string serial,
		AdbGatewayDeviceState state = AdbGatewayDeviceState.Device,
		string? model = null,
		string? manufacturer = null)
		=> new(serial, state, model, manufacturer, TunnelEstablished: true);

	[Test]
	public void ProvidedVariables_is_exactly_the_documented_eleven_names()
	{
		Assert.That(AdbVariables.All.Select(variable => variable.Name),
			Is.EquivalentTo(_expectedNames));
	}

	[Test]
	public void ProvidedVariables_is_constant_regardless_of_gateway_state()
	{
		var beforeNames = AdbVariables.All.Select(variable => variable.Name).ToList();

		_gateway.IsEnabled = false;
		_gateway.Devices = [Device("S1")];

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
	public async Task A_null_gateway_makes_every_variable_unavailable()
	{
		Assert.Multiple(async () =>
		{
			foreach (var variable in AdbVariables.All)
			{
				Assert.That((await AdbVariables.ReadAsync(null, variable.ResolvedId!, CancellationToken.None)).Value,
					Is.Null,
					variable.Name);
			}
		});
	}

	[Test]
	public async Task A_disabled_gateway_makes_every_variable_unavailable_including_the_counts()
	{
		_gateway.IsEnabled = false;
		_gateway.Devices = [Device("S1")];

		Assert.Multiple(async () =>
		{
			foreach (var variable in AdbVariables.All)
			{
				Assert.That(
					(await AdbVariables.ReadAsync(_gateway, variable.ResolvedId!, CancellationToken.None)).Value,
					Is.Null,
					variable.Name);
			}
		});
	}

	[Test]
	public async Task ReadAsync_never_calls_a_gateway_command_method()
	{
		_gateway.Devices = [Device("S1")];
		_gateway.DefaultDeviceSerial = "S1";
		_gateway.NextProperties = new AdbGatewayProperties(80, true, false, "com.example.app");

		foreach (var variable in AdbVariables.All)
		{
			_ = (await AdbVariables.ReadAsync(_gateway, variable.ResolvedId!, CancellationToken.None)).Value;
		}

		Assert.Multiple(() =>
		{
			Assert.That(_gateway.Calls, Has.Count.EqualTo(4));
			Assert.That(_gateway.Calls,
				Has.All.Matches<string>(call => call.StartsWith("GetProperties:", StringComparison.Ordinal)));
		});
	}

	[Test]
	public async Task Device_counts_reflect_state_regardless_of_which_device_is_default()
	{
		_gateway.Devices =
		[
			Device("S1", AdbGatewayDeviceState.Device),
			Device("S2", AdbGatewayDeviceState.Unauthorized),
			Device("S3", AdbGatewayDeviceState.Disconnected)
		];

		Assert.Multiple(async () =>
		{
			Assert.That(
				(await AdbVariables.ReadAsync(_gateway, AdbVariables.ConnectedDeviceCount, CancellationToken.None))
				.Value,
				Is.EqualTo(2));
			Assert.That((await AdbVariables.ReadAsync(_gateway,
					AdbVariables.AuthorizedDeviceCount,
					CancellationToken.None)).Value,
				Is.EqualTo(1));
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
		_gateway.Devices =
		[
			Device("S1", AdbGatewayDeviceState.Device, "Pixel 8", "Google"),
			Device("S2", AdbGatewayDeviceState.Device, "Galaxy S24", "Samsung")
		];
		_gateway.DefaultDeviceSerial = "S2";

		Assert.Multiple(async () =>
		{
			Assert.That(
				(await AdbVariables.ReadAsync(_gateway, AdbVariables.DeviceModel, CancellationToken.None)).Value,
				Is.EqualTo("Galaxy S24"));
			Assert.That(
				(await AdbVariables.ReadAsync(_gateway, AdbVariables.DeviceManufacturer, CancellationToken.None)).Value,
				Is.EqualTo("Samsung"));
		});
	}

	[Test]
	public async Task Device_fields_fall_back_to_the_single_connected_device_when_nothing_is_configured()
	{
		_gateway.Devices = [Device("S1", AdbGatewayDeviceState.Device, "Pixel 8")];

		Assert.Multiple(async () =>
		{
			Assert.That((await AdbVariables.ReadAsync(_gateway, AdbVariables.DeviceConnected, CancellationToken.None))
				.Value,
				Is.True);
			Assert.That(
				(await AdbVariables.ReadAsync(_gateway, AdbVariables.DeviceModel, CancellationToken.None)).Value,
				Is.EqualTo("Pixel 8"));
		});
	}

	[Test]
	public async Task Device_fields_are_unavailable_when_several_devices_are_connected_and_none_is_configured()
	{
		_gateway.Devices =
		[
			Device("S1", AdbGatewayDeviceState.Device),
			Device("S2", AdbGatewayDeviceState.Device)
		];

		Assert.Multiple(async () =>
		{
			Assert.That((await AdbVariables.ReadAsync(_gateway, AdbVariables.DeviceConnected, CancellationToken.None))
				.Value,
				Is.Null);
			Assert.That(
				(await AdbVariables.ReadAsync(_gateway, AdbVariables.DeviceModel, CancellationToken.None)).Value,
				Is.Null);
		});
	}

	[Test]
	public async Task A_configured_but_now_disconnected_device_reads_connected_as_false_not_unavailable()
	{
		_gateway.Devices = [Device("S1", AdbGatewayDeviceState.Disconnected)];
		_gateway.DefaultDeviceSerial = "S1";

		Assert.That(
			(await AdbVariables.ReadAsync(_gateway, AdbVariables.DeviceConnected, CancellationToken.None)).Value,
			Is.False);
	}

	[Test]
	public async Task Property_backed_fields_read_through_GetPropertiesAsync()
	{
		_gateway.NextProperties = new AdbGatewayProperties(42, true, false, "com.example.app");

		Assert.Multiple(async () =>
		{
			Assert.That(
				(await AdbVariables.ReadAsync(_gateway, AdbVariables.DeviceBatteryLevel, CancellationToken.None)).Value,
				Is.EqualTo(42));
			Assert.That((await AdbVariables.ReadAsync(_gateway, AdbVariables.DeviceScreenOn, CancellationToken.None))
				.Value,
				Is.True);
			Assert.That((await AdbVariables.ReadAsync(_gateway, AdbVariables.DeviceLocked, CancellationToken.None))
				.Value,
				Is.False);
			Assert.That(
				(await AdbVariables.ReadAsync(_gateway, AdbVariables.DeviceForegroundApp, CancellationToken.None))
				.Value,
				Is.EqualTo("com.example.app"));
		});
	}

	[Test]
	public async Task Property_backed_fields_are_unavailable_when_the_manager_has_no_sample_yet()
	{
		_gateway.NextProperties = null;

		Assert.That((await AdbVariables.ReadAsync(_gateway, AdbVariables.DeviceBatteryLevel, CancellationToken.None))
			.Value,
			Is.Null);
	}

	[Test]
	public async Task An_unknown_variable_name_is_unavailable()
	{
		_gateway.Devices = [Device("S1")];

		Assert.That((await AdbVariables.ReadAsync(_gateway, "not_a_real_variable", CancellationToken.None)).Value,
			Is.Null);
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
