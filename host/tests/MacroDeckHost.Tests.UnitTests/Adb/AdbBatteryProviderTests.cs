using System.Reflection;
using MacroDeck.Sdk.Android;
using MacroDeck.Sdk.Batteries;
using MacroDeckHost.Integrations.Adb.Providers;
using MacroDeckHost.Tests.UnitTests.Delegation;

namespace MacroDeckHost.Tests.UnitTests.Adb;

[TestFixture]
public class AdbBatteryProviderTests
{
	private const string StableId = "3fa9c2d1e0b4a756";
	private const string UsbSerial = "R5CT20ABCDE";
	private const string WifiSerial = "192.168.1.20:5555";
	private const string WifiSerialAfterPortChange = "192.168.1.20:40213";

	private ScriptedAndroidDevices _devices = null!;
	private FakeTimeProvider _time = null!;
	private RecordingBatteryProviderContext _context = null!;
	private AdbBatteryProvider _provider = null!;

	[SetUp]
	public void SetUp()
	{
		_devices = new ScriptedAndroidDevices();
		_time = new FakeTimeProvider();
		_context = new RecordingBatteryProviderContext();
		_provider = new AdbBatteryProvider(() => _devices, _time);
	}

	[TearDown]
	public void TearDown()
	{
		_provider.Dispose();
	}

	private static AndroidBatteryState Battery(
		int level,
		AndroidBatteryStatus status = AndroidBatteryStatus.Discharging)
	{
		return new AndroidBatteryState
		{
			Level = level,
			IsCharging = status == AndroidBatteryStatus.Charging,
			Status = status,
			Health = AndroidBatteryHealth.Good
		};
	}

	private static async Task WaitUntil(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (!condition() && DateTime.UtcNow < deadline)
		{
			await Task.Delay(10);
		}

		Assert.That(condition(), Is.True, "timed out waiting for the provider");
	}

	private async Task<int> SettledPublishCount()
	{
		var count = -1;
		while (count != _context.Publishes.Count)
		{
			count = _context.Publishes.Count;
			await Task.Delay(100);
		}

		return count;
	}

	[Test]
	public async Task A_connected_phone_is_published_under_its_stable_id_with_its_battery()
	{
		await _provider.InitializeAsync(_context, CancellationToken.None);

		_devices.Attach(UsbSerial, StableId, battery: Battery(81, AndroidBatteryStatus.Charging));

		await WaitUntil(() => _context.Latest(StableId) is not null);
		var published = _context.Latest(StableId)!;
		Assert.Multiple(() =>
		{
			Assert.That(published.Name, Is.EqualTo("Pixel 8"));
			Assert.That(published.Kind, Is.EqualTo(BatteryDeviceKinds.Phone));
			Assert.That(published.LevelPercent, Is.EqualTo(81));
			Assert.That(published.ChargeState, Is.EqualTo(BatteryChargeState.Charging));
			Assert.That(published.Health, Is.EqualTo(BatteryHealth.Good));
			Assert.That(published.IsConnected, Is.True);
			Assert.That(published.ToString(), Does.Not.Contain(UsbSerial), "the serial must never be published");
		});
	}

	[Test]
	public async Task Devices_already_attached_are_published_together_and_a_tablet_is_told_apart()
	{
		const string tabletId = "0000aaaa1111bbbb";
		_devices.Attach(UsbSerial, StableId, false);
		var tablet = _devices.Attach("TABLET01", tabletId, false);
		tablet.Characteristics = "nosdcard,tablet";
		_devices.Attach("NEWPHONE", StableId + "0", false).State = AndroidDeviceState.Unauthorized;
		_devices.Attach("NOIDYET", null, false);

		await _provider.InitializeAsync(_context, CancellationToken.None);

		var batch = _context.Publishes.Single();
		Assert.Multiple(() =>
		{
			Assert.That(batch.Select(device => device.Id), Is.EquivalentTo(new[] { StableId, tabletId }));
			Assert.That(batch.Single(device => device.Id == tabletId).Kind, Is.EqualTo(BatteryDeviceKinds.Tablet));
		});
	}

	[Test]
	public async Task A_device_is_published_once_it_reports_a_stable_id()
	{
		var phone = _devices.Attach(UsbSerial, null, false);
		await _provider.InitializeAsync(_context, CancellationToken.None);
		Assert.That(_context.Publishes, Is.Empty);

		phone.Info = phone.Info with { StableId = StableId };
		_time.Advance(AdbBatteryProvider.PollInterval);

		await WaitUntil(() => _context.Latest(StableId) is { IsConnected: true });
	}

	[Test]
	public async Task A_disconnected_phone_stays_published_as_not_connected()
	{
		_devices.Attach(UsbSerial, StableId, false).Battery = Battery(64);
		await _provider.InitializeAsync(_context, CancellationToken.None);

		_devices.Detach(UsbSerial);

		await WaitUntil(() => _context.Latest(StableId)?.IsConnected == false);
		Assert.Multiple(() =>
		{
			Assert.That(_context.Latest(StableId)!.LevelPercent, Is.EqualTo(64), "the last known level is kept");
			Assert.That(_context.Removed, Is.Empty, "a disconnect is not a removal");
		});
	}

	[Test]
	public async Task A_phone_reachable_over_usb_and_wifi_is_connected_until_its_last_connection_drops()
	{
		await _provider.InitializeAsync(_context, CancellationToken.None);
		_devices.Attach(UsbSerial, StableId);
		_devices.Attach(WifiSerial, StableId);
		await WaitUntil(() => _context.Latest(StableId) is not null);

		_devices.Detach(UsbSerial);
		_devices.Attach(UsbSerial, StableId);
		_devices.Detach(WifiSerial);
		var publishesBeforeLastDrop = await SettledPublishCount();

		Assert.That(_context.Publishes.SelectMany(batch => batch).Any(device => !device.IsConnected),
			Is.False,
			"one remaining connection keeps the phone connected");

		_devices.Detach(UsbSerial);

		await WaitUntil(() => _context.Publishes.Count > publishesBeforeLastDrop);
		Assert.That(_context.Latest(StableId)!.IsConnected, Is.False);
	}

	[Test]
	public async Task A_wifi_port_change_reported_before_the_old_port_drops_keeps_the_phone_connected()
	{
		await _provider.InitializeAsync(_context, CancellationToken.None);
		_devices.Attach(WifiSerial, StableId);

		_devices.Attach(WifiSerialAfterPortChange, StableId);
		_devices.Detach(WifiSerial);
		await SettledPublishCount();

		Assert.Multiple(() =>
		{
			Assert.That(_context.Latest(StableId), Is.Not.Null);
			Assert.That(_context.Publishes.SelectMany(batch => batch).All(device => device.IsConnected), Is.True);
		});
	}

	[Test]
	public async Task Turning_adb_off_shows_every_phone_as_not_connected()
	{
		_devices.Attach(UsbSerial, StableId, false).Battery = Battery(42);
		await _provider.InitializeAsync(_context, CancellationToken.None);

		_devices.SetAccess(AndroidDeviceAccess.AdbNotEnabled);

		await WaitUntil(() => _context.Latest(StableId)?.IsConnected == false);
		Assert.That(_context.Latest(StableId)!.LevelPercent, Is.EqualTo(42));
	}

	[Test]
	public async Task A_phone_that_vanishes_without_an_event_is_shown_as_not_connected_on_the_next_poll()
	{
		_devices.Attach(UsbSerial, StableId, false);
		await _provider.InitializeAsync(_context, CancellationToken.None);

		_devices.Detach(UsbSerial, false);
		_time.Advance(AdbBatteryProvider.PollInterval);

		await WaitUntil(() => _context.Latest(StableId)?.IsConnected == false);
	}

	[Test]
	public async Task A_refused_tablet_probe_publishes_a_phone_and_is_asked_again_on_the_next_poll()
	{
		var tablet = _devices.Attach(UsbSerial, StableId, false);
		tablet.Characteristics = "tablet";
		tablet.ShellFailure = AndroidDeviceErrorCode.HostLocked;
		await _provider.InitializeAsync(_context, CancellationToken.None);
		Assert.That(_context.Latest(StableId)!.Kind, Is.EqualTo(BatteryDeviceKinds.Phone));

		tablet.ShellFailure = null;
		_time.Advance(AdbBatteryProvider.PollInterval);
		await WaitUntil(() => _context.Latest(StableId)?.Kind == BatteryDeviceKinds.Tablet);

		tablet.Battery = Battery(12);
		_time.Advance(AdbBatteryProvider.PollInterval);
		await WaitUntil(() => _context.Latest(StableId)?.LevelPercent == 12);
		Assert.That(tablet.Commands.Count(command => command == AdbBatteryProvider.CharacteristicsCommand),
			Is.EqualTo(2),
			"an answered probe is not repeated");
	}

	[Test]
	public async Task Polling_publishes_a_changed_level_every_fifteen_seconds()
	{
		var phone = _devices.Attach(UsbSerial, StableId, false);
		phone.Battery = Battery(90);
		await _provider.InitializeAsync(_context, CancellationToken.None);

		phone.Battery = Battery(89);
		_time.Advance(AdbBatteryProvider.PollInterval);

		await WaitUntil(() => _context.Latest(StableId)?.LevelPercent == 89);
	}

	[Test]
	public async Task Shutdown_stops_polling_and_ignores_later_device_changes()
	{
		var phone = _devices.Attach(UsbSerial, StableId, false);
		await _provider.InitializeAsync(_context, CancellationToken.None);

		await _provider.ShutdownAsync(CancellationToken.None);
		phone.Battery = Battery(10);
		_time.Advance(AdbBatteryProvider.PollInterval * 3);
		_devices.Detach(UsbSerial);
		await Task.Delay(100);

		Assert.Multiple(() =>
		{
			Assert.That(_time.ActiveTimerCount, Is.Zero);
			Assert.That(_context.Publishes, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public void The_provider_reaches_the_host_only_through_sdk_types()
	{
		var hostTypes = TypesReachedBy(typeof(AdbBatteryProvider))
			.Where(type => type.Namespace?.StartsWith("MacroDeckHost", StringComparison.Ordinal) == true)
			.Where(type => type != typeof(AdbBatteryProvider) && !IsNestedIn(type, typeof(AdbBatteryProvider)))
			.ToList();

		Assert.That(hostTypes, Is.Empty);
	}

	// Signatures, fields and the state machines of async methods, whose fields hold their locals and awaiters.
	private static HashSet<Type> TypesReachedBy(Type root)
	{
		const BindingFlags all = BindingFlags.Public
			| BindingFlags.NonPublic
			| BindingFlags.Instance
			| BindingFlags.Static
			| BindingFlags.DeclaredOnly;
		var seen = new HashSet<Type>();
		var pending = new Stack<Type>([root]);
		while (pending.TryPop(out var type))
		{
			foreach (var nested in type.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public))
			{
				pending.Push(nested);
			}

			var referenced = type.GetFields(all)
				.Select(field => field.FieldType)
				.Concat(type.GetMethods(all)
					.SelectMany(method => method.GetParameters()
						.Select(parameter => parameter.ParameterType)
						.Append(method.ReturnType)))
				.Concat(type.GetConstructors(all)
					.SelectMany(constructor => constructor.GetParameters()
						.Select(parameter => parameter.ParameterType)));
			foreach (var reference in referenced.SelectMany(Flatten))
			{
				seen.Add(reference);
			}
		}

		return seen;
	}

	private static IEnumerable<Type> Flatten(Type type)
	{
		var element = type.HasElementType ? type.GetElementType()! : type;
		yield return element.IsGenericType ? element.GetGenericTypeDefinition() : element;
		foreach (var argument in element.IsGenericType ? element.GetGenericArguments() : [])
		{
			foreach (var inner in Flatten(argument))
			{
				yield return inner;
			}
		}
	}

	private static bool IsNestedIn(Type type, Type outer)
	{
		return type.DeclaringType is { } declaring && (declaring == outer || IsNestedIn(declaring, outer));
	}
}
