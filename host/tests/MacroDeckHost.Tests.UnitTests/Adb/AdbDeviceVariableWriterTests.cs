using MacroDeck.Sdk.Variables;
using MacroDeckHost.Integrations.Adb.Connections;
using MacroDeckHost.Integrations.Adb.Variables;

namespace MacroDeckHost.Tests.UnitTests.Adb;

[TestFixture]
internal sealed class AdbDeviceVariableWriterTests
{
	private readonly Dictionary<string, ScriptedAndroidDevice> _phones = new(StringComparer.Ordinal);
	private RecordingVariableApi _variables = null!;
	private List<AdbTrackedDevice> _devices = null!;

	[SetUp]
	public void SetUp()
	{
		_phones.Clear();
		_variables = new RecordingVariableApi();
		_devices = [];
	}

	private ScriptedAndroidDevice Phone(string serial)
	{
		if (!_phones.TryGetValue(serial, out var phone))
		{
			phone = new ScriptedAndroidDevice(serial, null);
			_phones[serial] = phone;
		}

		return phone;
	}

	private void SetBatteryLevel(string serial, int level)
	{
		Phone(serial).Battery = Phone(serial).Battery with { Level = level };
	}

	private AdbTrackedDevice Device(
		string serial = "ABC123",
		AdbDeviceStatus state = AdbDeviceStatus.Device,
		string? model = "Pixel 8")
	{
		return new AdbTrackedDevice(Phone(serial), state, model, "Google");
	}

	[Test]
	public async Task Authorization_creates_all_four_variables_named_after_the_model()
	{
		SetBatteryLevel("ABC123", 77);

		await AdbDeviceVariableWriter.HandleAsync(_variables,
			_devices,
			new AdbDeviceChange(AdbDeviceChangeKind.Authorized, Device("ABC123")),
			CancellationToken.None);

		await Assert.MultipleAsync(async () =>
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
			_devices,
			new AdbDeviceChange(AdbDeviceChangeKind.Connected,
				Device(state: AdbDeviceStatus.Unauthorized)),
			CancellationToken.None);

		Assert.That(_variables.CreateCount, Is.EqualTo(0));
	}

	[Test]
	public async Task A_later_transition_updates_an_already_authorized_devices_variables()
	{
		await AdbDeviceVariableWriter.HandleAsync(_variables,
			_devices,
			new AdbDeviceChange(AdbDeviceChangeKind.Authorized, Device()),
			CancellationToken.None);

		SetBatteryLevel("ABC123", 50);
		await AdbDeviceVariableWriter.HandleAsync(_variables,
			_devices,
			new AdbDeviceChange(AdbDeviceChangeKind.Offline,
				Device(state: AdbDeviceStatus.Offline)),
			CancellationToken.None);

		await Assert.MultipleAsync(async () =>
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
			_devices,
			new AdbDeviceChange(AdbDeviceChangeKind.Authorized, Device()),
			CancellationToken.None);

		await AdbDeviceVariableWriter.HandleAsync(_variables,
			_devices,
			new AdbDeviceChange(AdbDeviceChangeKind.Disconnected,
				Device(state: AdbDeviceStatus.Disconnected)),
			CancellationToken.None);

		await Assert.MultipleAsync(async () =>
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
			_devices,
			new AdbDeviceChange(AdbDeviceChangeKind.Disconnected,
				Device(state: AdbDeviceStatus.Disconnected)),
			CancellationToken.None);

		Assert.That(_variables.CreateCount, Is.EqualTo(0));
	}

	[Test]
	public async Task Reauthorizing_after_a_disconnect_reuses_the_existing_variables_rather_than_recreating_them()
	{
		await AdbDeviceVariableWriter.HandleAsync(_variables,
			_devices,
			new AdbDeviceChange(AdbDeviceChangeKind.Authorized, Device()),
			CancellationToken.None);
		await AdbDeviceVariableWriter.HandleAsync(_variables,
			_devices,
			new AdbDeviceChange(AdbDeviceChangeKind.Disconnected,
				Device(state: AdbDeviceStatus.Disconnected)),
			CancellationToken.None);

		var createCountAfterFirstRun = _variables.CreateCount;

		await AdbDeviceVariableWriter.HandleAsync(_variables,
			_devices,
			new AdbDeviceChange(AdbDeviceChangeKind.Authorized, Device()),
			CancellationToken.None);

		await Assert.MultipleAsync(async () =>
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
				_devices,
				new AdbDeviceChange(AdbDeviceChangeKind.Authorized, Device()),
				CancellationToken.None);
		});
	}

	[Test]
	public async Task The_model_is_sanitized_into_the_variable_name()
	{
		await AdbDeviceVariableWriter.HandleAsync(_variables,
			_devices,
			new AdbDeviceChange(AdbDeviceChangeKind.Authorized,
				Device("192.168.1.5:5555", model: "Galaxy S24 Ultra")),
			CancellationToken.None);

		Assert.That(await _variables.GetByNameAsync("adb_device_galaxy_s24_ultra_connected"), Is.Not.Null);
	}

	[Test]
	public async Task No_variable_name_ever_contains_the_serial()
	{
		var serial = "PBZDWSKV796PIBXW";
		_devices = [Device(serial, model: null)];

		await AdbDeviceVariableWriter.HandleAsync(_variables,
			_devices,
			new AdbDeviceChange(AdbDeviceChangeKind.Authorized, Device(serial, model: null)),
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
		_devices = [second, first];

		await AdbDeviceVariableWriter.HandleAsync(_variables,
			_devices,
			new AdbDeviceChange(AdbDeviceChangeKind.Authorized, first),
			CancellationToken.None);
		await AdbDeviceVariableWriter.HandleAsync(_variables,
			_devices,
			new AdbDeviceChange(AdbDeviceChangeKind.Authorized, second),
			CancellationToken.None);

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(await _variables.GetByNameAsync("adb_device_pixel_8_connected"), Is.Not.Null);
			Assert.That(await _variables.GetByNameAsync("adb_device_pixel_8_2_connected"), Is.Not.Null);
		});
	}
}
