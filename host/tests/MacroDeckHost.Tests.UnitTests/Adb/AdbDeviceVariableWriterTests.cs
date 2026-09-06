using MacroDeckHost.Integrations.Adb;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Tests.UnitTests.Adb;

[TestFixture]
internal sealed class AdbDeviceVariableWriterTests
{
	private FakeAdbGateway _gateway = null!;
	private RecordingVariableApi _variables = null!;

	[SetUp]
	public void SetUp()
	{
		_gateway = new FakeAdbGateway();
		_variables = new RecordingVariableApi();
	}

	private static AdbGatewayDevice Device(
		string serial = "ABC123",
		AdbGatewayDeviceState state = AdbGatewayDeviceState.Device,
		string? model = "Pixel 8")
		=> new(serial, state, model, Manufacturer: "Google", TunnelEstablished: true);

	[Test]
	public async Task Authorization_creates_all_four_variables_named_after_the_model()
	{
		_gateway.NextProperties = new AdbGatewayProperties(77, true, false, "com.example.app");

		await AdbDeviceVariableWriter.HandleAsync(_variables,
			_gateway,
			new AdbGatewayDeviceChange(AdbGatewayDeviceChangeKind.Authorized, Device("ABC123")),
			CancellationToken.None);

		Assert.Multiple(async () =>
		{
			Assert.That((await _variables.GetByNameAsync("adb_device_pixel_8_connected"))!.Value, Is.EqualTo(true));
			Assert.That((await _variables.GetByNameAsync("adb_device_pixel_8_authorized"))!.Value, Is.EqualTo(true));
			Assert.That((await _variables.GetByNameAsync("adb_device_pixel_8_model"))!.Value, Is.EqualTo("Pixel 8"));
			Assert.That((await _variables.GetByNameAsync("adb_device_pixel_8_battery_level"))!.Value, Is.EqualTo(77));
		});
	}

	[Test]
	public async Task A_connected_but_never_authorized_device_creates_nothing()
	{
		await AdbDeviceVariableWriter.HandleAsync(_variables,
			_gateway,
			new AdbGatewayDeviceChange(AdbGatewayDeviceChangeKind.Connected,
				Device(state: AdbGatewayDeviceState.Unauthorized)),
			CancellationToken.None);

		Assert.That(_variables.CreateCount, Is.EqualTo(0));
	}

	[Test]
	public async Task A_later_transition_updates_an_already_authorized_devices_variables()
	{
		await AdbDeviceVariableWriter.HandleAsync(_variables,
			_gateway,
			new AdbGatewayDeviceChange(AdbGatewayDeviceChangeKind.Authorized, Device()),
			CancellationToken.None);

		_gateway.NextProperties = new AdbGatewayProperties(50, false, true, null);
		await AdbDeviceVariableWriter.HandleAsync(_variables,
			_gateway,
			new AdbGatewayDeviceChange(AdbGatewayDeviceChangeKind.Offline,
				Device(state: AdbGatewayDeviceState.Offline)),
			CancellationToken.None);

		Assert.Multiple(async () =>
		{
			Assert.That((await _variables.GetByNameAsync("adb_device_pixel_8_connected"))!.Value, Is.EqualTo(true));
			Assert.That((await _variables.GetByNameAsync("adb_device_pixel_8_authorized"))!.Value, Is.EqualTo(false));
			Assert.That((await _variables.GetByNameAsync("adb_device_pixel_8_battery_level"))!.Value, Is.EqualTo(50));
			Assert.That(_variables.CreateCount, Is.EqualTo(4), "only the first, Authorized transition should create");
		});
	}

	[Test]
	public async Task Disconnect_clears_the_four_variables_to_null_instead_of_deleting_them()
	{
		await AdbDeviceVariableWriter.HandleAsync(_variables,
			_gateway,
			new AdbGatewayDeviceChange(AdbGatewayDeviceChangeKind.Authorized, Device()),
			CancellationToken.None);

		await AdbDeviceVariableWriter.HandleAsync(_variables,
			_gateway,
			new AdbGatewayDeviceChange(AdbGatewayDeviceChangeKind.Disconnected,
				Device(state: AdbGatewayDeviceState.Disconnected)),
			CancellationToken.None);

		Assert.Multiple(async () =>
		{
			var connected = await _variables.GetByNameAsync("adb_device_pixel_8_connected");
			Assert.That(connected, Is.Not.Null, "cleared, not deleted");
			Assert.That(connected!.Value, Is.Null);

			var authorized = await _variables.GetByNameAsync("adb_device_pixel_8_authorized");
			Assert.That(authorized!.Value, Is.Null);

			var model = await _variables.GetByNameAsync("adb_device_pixel_8_model");
			Assert.That(model!.Value, Is.Null);

			var battery = await _variables.GetByNameAsync("adb_device_pixel_8_battery_level");
			Assert.That(battery!.Value, Is.Null);
		});
	}

	[Test]
	public async Task Disconnecting_a_device_that_was_never_authorized_does_nothing()
	{
		await AdbDeviceVariableWriter.HandleAsync(_variables,
			_gateway,
			new AdbGatewayDeviceChange(AdbGatewayDeviceChangeKind.Disconnected,
				Device(state: AdbGatewayDeviceState.Disconnected)),
			CancellationToken.None);

		Assert.That(_variables.CreateCount, Is.EqualTo(0));
	}

	[Test]
	public async Task Reauthorizing_after_a_disconnect_reuses_the_existing_variables_rather_than_recreating_them()
	{
		await AdbDeviceVariableWriter.HandleAsync(_variables,
			_gateway,
			new AdbGatewayDeviceChange(AdbGatewayDeviceChangeKind.Authorized, Device()),
			CancellationToken.None);
		await AdbDeviceVariableWriter.HandleAsync(_variables,
			_gateway,
			new AdbGatewayDeviceChange(AdbGatewayDeviceChangeKind.Disconnected,
				Device(state: AdbGatewayDeviceState.Disconnected)),
			CancellationToken.None);

		var createCountAfterFirstRun = _variables.CreateCount;

		await AdbDeviceVariableWriter.HandleAsync(_variables,
			_gateway,
			new AdbGatewayDeviceChange(AdbGatewayDeviceChangeKind.Authorized, Device()),
			CancellationToken.None);

		Assert.Multiple(async () =>
		{
			Assert.That(_variables.CreateCount,
				Is.EqualTo(createCountAfterFirstRun),
				"get-or-create must not throw a collision");
			Assert.That((await _variables.GetByNameAsync("adb_device_pixel_8_connected"))!.Value, Is.EqualTo(true));
		});
	}

	[Test]
	public void A_pre_existing_variable_with_the_same_name_never_throws()
	{
		Assert.DoesNotThrowAsync(async () =>
		{
			await _variables.CreateAsync("adb_device_pixel_8_connected", VariableType.Boolean);
			await AdbDeviceVariableWriter.HandleAsync(_variables,
				_gateway,
				new AdbGatewayDeviceChange(AdbGatewayDeviceChangeKind.Authorized, Device()),
				CancellationToken.None);
		});
	}

	[Test]
	public async Task The_model_is_sanitized_into_the_variable_name()
	{
		await AdbDeviceVariableWriter.HandleAsync(_variables,
			_gateway,
			new AdbGatewayDeviceChange(AdbGatewayDeviceChangeKind.Authorized,
				Device("192.168.1.5:5555", model: "Galaxy S24 Ultra")),
			CancellationToken.None);

		Assert.That(await _variables.GetByNameAsync("adb_device_galaxy_s24_ultra_connected"), Is.Not.Null);
	}

	[Test]
	public async Task No_variable_name_ever_contains_the_serial()
	{
		var serial = "PBZDWSKV796PIBXW";
		_gateway.Devices = [Device(serial, model: null)];

		await AdbDeviceVariableWriter.HandleAsync(_variables,
			_gateway,
			new AdbGatewayDeviceChange(AdbGatewayDeviceChangeKind.Authorized, Device(serial, model: null)),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_variables.Names, Is.Not.Empty);
			Assert.That(_variables.Names,
				Has.None.Contains(serial.ToLowerInvariant()),
				"a device without a model must fall back to a generic stem, never the serial");
		});
	}

	[Test]
	public async Task Two_devices_of_the_same_model_get_distinct_names_ordered_by_serial()
	{
		var first = Device("AAA111");
		var second = Device("BBB222");
		_gateway.Devices = [second, first];

		await AdbDeviceVariableWriter.HandleAsync(_variables,
			_gateway,
			new AdbGatewayDeviceChange(AdbGatewayDeviceChangeKind.Authorized, first),
			CancellationToken.None);
		await AdbDeviceVariableWriter.HandleAsync(_variables,
			_gateway,
			new AdbGatewayDeviceChange(AdbGatewayDeviceChangeKind.Authorized, second),
			CancellationToken.None);

		Assert.Multiple(async () =>
		{
			Assert.That(await _variables.GetByNameAsync("adb_device_pixel_8_connected"), Is.Not.Null);
			Assert.That(await _variables.GetByNameAsync("adb_device_pixel_8_2_connected"), Is.Not.Null);
		});
	}
}
