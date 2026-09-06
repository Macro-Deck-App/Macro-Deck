using MacroDeckHost.Application.Devices;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.Triggers;

namespace MacroDeckHost.Tests.UnitTests.Devices;

[TestFixture]
public class DeviceConnectionTrackerTests
{
	private static readonly string[] _connectedThenDisconnected =
		["macro-deck::client-connected", "macro-deck::client-disconnected"];

	private static readonly string[] _connectedOnly = ["macro-deck::client-connected"];

	private RecordingEventBus _bus = null!;
	private ManualTimeProvider _time = null!;
	private DeviceConnectionTracker _tracker = null!;

	[SetUp]
	public void SetUp()
	{
		_bus = new RecordingEventBus();
		_time = new ManualTimeProvider();
		_tracker = new DeviceConnectionTracker(_bus, _time);
	}

	private void AdvancePastLingerAndFlush()
	{
		_time.Advance(TimeSpan.FromSeconds(DeviceDefaults.PresenceLingerSeconds + 1));
		_tracker.FlushPendingDisconnects(_time.Now.UtcDateTime);
	}

	[Test]
	public void Registering_a_client_raises_client_connected()
	{
		_tracker.Register("conn-1", "client-a");

		Assert.Multiple(() =>
		{
			Assert.That(_bus.Published, Has.Count.EqualTo(1));
			Assert.That(_bus.Published[0].EventId, Is.EqualTo("macro-deck::client-connected"));
			Assert.That(_bus.Published[0].Parameters["clientId"], Is.EqualTo("client-a"));
		});
	}

	[Test]
	public void Disconnecting_the_last_connection_raises_client_disconnected()
	{
		_tracker.Register("conn-1", "client-a");
		_tracker.Remove("conn-1");
		AdvancePastLingerAndFlush();

		Assert.That(_bus.Published.Select(o => o.EventId), Is.EqualTo(_connectedThenDisconnected));
	}

	[Test]
	public void A_reconnect_does_not_raise_a_second_connected_event()
	{
		_tracker.Register("conn-1", "client-a");
		_tracker.Register("conn-2", "client-a");
		_tracker.Remove("conn-1");

		Assert.That(_bus.Published.Select(o => o.EventId), Is.EqualTo(_connectedOnly));
	}

	[Test]
	public void Disconnecting_the_last_of_several_connections_raises_disconnected_once()
	{
		_tracker.Register("conn-1", "client-a");
		_tracker.Register("conn-2", "client-a");
		_tracker.Remove("conn-1");
		_tracker.Remove("conn-2");
		AdvancePastLingerAndFlush();

		Assert.That(_bus.Published.Count(o => o.EventId == "macro-deck::client-disconnected"), Is.EqualTo(1));
	}

	[Test]
	public void Two_clients_are_tracked_independently()
	{
		_tracker.Register("conn-1", "client-a");
		_tracker.Register("conn-2", "client-b");
		_tracker.Remove("conn-1");
		AdvancePastLingerAndFlush();

		Assert.That(_bus.Published.Where(o => o.EventId == "macro-deck::client-disconnected")
				.Select(o => o.Parameters["clientId"]),
			Is.EqualTo(new object[] { "client-a" }));
	}

	[Test]
	public void A_connection_that_never_registered_raises_nothing_on_disconnect()
	{
		_tracker.Remove("conn-unknown");

		Assert.That(_bus.Published, Is.Empty);
	}

	[Test]
	public void An_empty_client_id_is_ignored()
	{
		_tracker.Register("conn-1", "  ");

		Assert.That(_bus.Published, Is.Empty);
	}

	[Test]
	public void A_second_Register_on_the_same_connection_does_not_leak_the_presence_count()
	{
		_tracker.Register("conn-1", "client-a");
		_tracker.Register("conn-1", "client-a");
		_tracker.Remove("conn-1");
		AdvancePastLingerAndFlush();

		Assert.That(_bus.Published.Select(o => o.EventId), Is.EqualTo(_connectedThenDisconnected));
	}

	[Test]
	public void Two_connections_on_one_device_raise_one_connected()
	{
		var deviceId = Guid.NewGuid();
		_tracker.Attach("conn-1", deviceId, () => { });
		_tracker.Register("conn-1", "tab-1");
		_tracker.Attach("conn-2", deviceId, () => { });
		_tracker.Register("conn-2", "tab-2");

		Assert.That(_bus.Published.Count(o => o.EventId == "macro-deck::client-connected"), Is.EqualTo(1));
	}

	[Test]
	public void A_reload_inside_the_linger_window_raises_neither_disconnect_nor_a_second_connected()
	{
		var deviceId = Guid.NewGuid();
		_tracker.Attach("conn-1", deviceId, () => { });
		_tracker.Register("conn-1", "tab-1");
		_tracker.Remove("conn-1");

		_time.Advance(TimeSpan.FromSeconds(1));
		_tracker.Attach("conn-2", deviceId, () => { });
		_tracker.Register("conn-2", "tab-1");
		_tracker.FlushPendingDisconnects(_time.Now.UtcDateTime);

		Assert.That(_bus.Published.Select(o => o.EventId), Is.EqualTo(_connectedOnly));
	}

	[Test]
	public void A_genuine_disconnect_raises_after_the_window()
	{
		var deviceId = Guid.NewGuid();
		_tracker.Attach("conn-1", deviceId, () => { });
		_tracker.Register("conn-1", "tab-1");
		_tracker.Remove("conn-1");

		_tracker.FlushPendingDisconnects(_time.Now.UtcDateTime);
		Assert.That(_bus.Published.Select(o => o.EventId), Is.EqualTo(_connectedOnly), "not due yet");

		AdvancePastLingerAndFlush();
		Assert.That(_bus.Published.Select(o => o.EventId), Is.EqualTo(_connectedThenDisconnected));
	}

	[Test]
	public void OnlineDeviceConnectionCounts_still_reports_a_device_inside_the_linger_window()
	{
		var deviceId = Guid.NewGuid();
		_tracker.Attach("conn-1", deviceId, () => { });
		_tracker.Register("conn-1", "tab-1");
		_tracker.Remove("conn-1");

		var counts = _tracker.OnlineDeviceConnectionCounts();

		Assert.That(counts.GetValueOrDefault(deviceId), Is.GreaterThan(0));
	}

	[Test]
	public void OnlineDeviceConnectionCounts_drops_the_device_once_the_linger_window_lapses()
	{
		var deviceId = Guid.NewGuid();
		_tracker.Attach("conn-1", deviceId, () => { });
		_tracker.Register("conn-1", "tab-1");
		_tracker.Remove("conn-1");

		_time.Advance(TimeSpan.FromSeconds(DeviceDefaults.PresenceLingerSeconds + 1));

		var counts = _tracker.OnlineDeviceConnectionCounts();

		Assert.That(counts.ContainsKey(deviceId), Is.False);
	}

	[Test]
	public void OnlineDeviceConnectionCounts_reports_the_real_count_when_another_connection_is_still_live()
	{
		var deviceId = Guid.NewGuid();
		_tracker.Attach("conn-1", deviceId, () => { });
		_tracker.Register("conn-1", "tab-1");
		_tracker.Attach("conn-2", deviceId, () => { });
		_tracker.Register("conn-2", "tab-2");
		_tracker.Remove("conn-1");

		var counts = _tracker.OnlineDeviceConnectionCounts();

		Assert.That(counts[deviceId], Is.EqualTo(1));
	}

	[Test]
	public void Device_less_connections_key_on_client_id()
	{
		_tracker.Attach("conn-1", null, () => { });
		_tracker.Register("conn-1", "client-a");

		Assert.Multiple(() =>
		{
			Assert.That(_bus.Published[0].Parameters["clientId"], Is.EqualTo("client-a"));
			Assert.That(_bus.Published[0].Parameters["deviceId"], Is.Null);
		});
	}

	[Test]
	public void AbortDevice_invokes_only_that_devices_abort_delegates()
	{
		var deviceA = Guid.NewGuid();
		var deviceB = Guid.NewGuid();
		var aborted = new List<string>();

		_tracker.Attach("conn-1", deviceA, () => aborted.Add("conn-1"));
		_tracker.Attach("conn-2", deviceB, () => aborted.Add("conn-2"));

		_tracker.AbortDevice(deviceA);

		Assert.That(aborted, Is.EqualTo(new List<string> { "conn-1" }));
	}

	[Test]
	public void AbortAll_invokes_every_abort_delegate_regardless_of_device()
	{
		var aborted = new List<string>();

		_tracker.Attach("conn-1", Guid.NewGuid(), () => aborted.Add("conn-1"));
		_tracker.Attach("conn-2", null, () => aborted.Add("conn-2"));

		_tracker.AbortAll();

		Assert.That(aborted, Is.EquivalentTo(new List<string> { "conn-1", "conn-2" }));
	}

	[Test]
	public void The_payload_carries_device_id_device_name_and_client_id()
	{
		var deviceId = Guid.NewGuid();
		_tracker.SetDeviceNames(new Dictionary<Guid, string> { [deviceId] = "Kitchen tablet" });
		_tracker.Attach("conn-1", deviceId, () => { });

		_tracker.Register("conn-1", "tab-1");

		var parameters = _bus.Published[0].Parameters;
		Assert.Multiple(() =>
		{
			Assert.That(parameters["deviceId"], Is.EqualTo(deviceId.ToString()));
			Assert.That(parameters["deviceName"], Is.EqualTo("Kitchen tablet"));
			Assert.That(parameters["clientId"], Is.EqualTo("tab-1"));
		});
	}

	[Test]
	public void A_single_name_seeded_at_registration_survives_a_later_bulk_refresh()
	{
		var seeded = Guid.NewGuid();
		var other = Guid.NewGuid();
		_tracker.SetDeviceNames(new Dictionary<Guid, string> { [other] = "Old device" });

		_tracker.SetDeviceName(seeded, "Kitchen tablet");
		_tracker.Attach("conn-1", seeded, () => { });
		_tracker.Register("conn-1", "tab-1");

		Assert.Multiple(() =>
		{
			Assert.That(_bus.Published[0].Parameters["deviceName"], Is.EqualTo("Kitchen tablet"));
			Assert.That(_tracker.OnlineDeviceConnectionCounts()[seeded], Is.EqualTo(1));
		});
	}
}
