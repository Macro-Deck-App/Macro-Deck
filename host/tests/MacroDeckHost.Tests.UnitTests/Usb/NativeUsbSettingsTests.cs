using System.Reflection;
using MacroDeckHost.Application.Adb;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;
using MacroDeckHost.Application.Usb;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Infrastructure.Usb.Native;
using MacroDeckHost.Tests.UnitTests.Adb;
using MacroDeckHost.Tests.UnitTests.Delegation;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Usb;

public class NativeUsbSettingsTests
{
	private static readonly UsbInterfaceInfo _mtp = new(0, 0x06, 0x01, 0x01, 0);

	private sealed class MemoryPreferences : IAppPreferenceRepository
	{
		public Dictionary<string, string> Values { get; } = [];

		public Task<AppPreferenceEntity?> GetByKey(string key)
			=> Task.FromResult(Values.TryGetValue(key, out var value)
				? new AppPreferenceEntity { Key = key, Value = value }
				: null);

		public Task SetValue(string key, string value)
		{
			Values[key] = value;
			return Task.CompletedTask;
		}
	}

	private sealed class TestBuildEnvironment : IBuildEnvironment
	{
		public string Version => "0.0.0-test";

		public bool IsBeta => false;

		public BuildChannel Channel => BuildChannel.Production;
	}

	[Test]
	public async Task Usb_without_debugging_is_off_by_default_with_nothing_remembered()
	{
		var settings = await Preferences(new MemoryPreferences()).GetNativeUsb();

		Assert.Multiple(() =>
		{
			Assert.That(settings.Enabled, Is.False);
			Assert.That(settings.RememberedSerials, Is.Empty);
		});
	}

	[Test]
	public async Task Remembered_serials_round_trip_and_a_null_argument_keeps_the_stored_value()
	{
		var store = new MemoryPreferences();
		var preferences = Preferences(store);

		await preferences.SetNativeUsb(false,
			Remember("EXAMPLE0001", "EXAMPLE0001", "bad serial!", "EXAMPLE0002", "SN#01/ab+c", new string('x', 129)));
		await preferences.SetNativeUsb(null, null);
		var reloaded = await Preferences(store).GetNativeUsb();

		Assert.Multiple(() =>
		{
			Assert.That(reloaded.Enabled, Is.False);
			Assert.That(reloaded.RememberedSerials, Is.EqualTo(new[] { "EXAMPLE0001", "EXAMPLE0002", "SN#01/ab+c" }));
		});
	}

	[Test]
	public async Task While_it_is_off_nothing_touches_libusb_or_usbmuxd()
	{
		var fixture = Fixture(PublicEndpointSet.HttpOnly(8193), enabled: false);
		fixture.Usb.Plug(1, [_mtp]);

		await fixture.Manager.PollAsync(CancellationToken.None);
		await fixture.Manager.ApplySettingsAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(fixture.Usb.Enumerations, Is.Zero);
			Assert.That(fixture.Usb.AvailabilityChecks, Is.Zero);
			Assert.That(fixture.Usbmux.ListenCalls, Is.Zero);
			Assert.That(fixture.Manager.Status.Devices, Is.Empty);
		});
	}

	[Test]
	public async Task Turning_it_off_is_not_undone_by_remembering_a_phone_and_keeps_what_was_remembered()
	{
		var fixture = Fixture(PublicEndpointSet.HttpOnly(8193));
		fixture.Usb.Plug(1, [_mtp]);
		await fixture.Manager.PollAsync(CancellationToken.None);
		await fixture.Manager.PickAsync(fixture.Manager.Status.Devices.Single().Id, CancellationToken.None);
		fixture.Usb.SwitchToAccessoryMode();
		await fixture.Manager.PollAsync(CancellationToken.None);
		fixture.Sessions.Single().Link();
		await fixture.Manager.PollAsync(CancellationToken.None);

		await new UpdateNativeUsbSettingsRequestMessageHandler(fixture.Manager)
			.Handle(new UpdateNativeUsbSettingsRequest { Enabled = false }, CancellationToken.None);
		await fixture.Manager.PollAsync(CancellationToken.None);
		var stored = await Preferences(fixture.Store).GetNativeUsb();

		Assert.Multiple(() =>
		{
			Assert.That(stored.Enabled, Is.False);
			Assert.That(stored.RememberedSerials, Is.EqualTo(new[] { "EXAMPLE0001" }));
		});
	}

	[Test]
	public async Task Shutdown_returns_when_the_stop_token_fires_even_if_a_link_is_still_writing()
	{
		var fixture = Fixture(PublicEndpointSet.HttpOnly(8193), sessionsEndWhenStopped: false);
		await Preferences(fixture.Store).SetNativeUsb(true, Remember("EXAMPLE0001"));
		fixture.Usb.SwitchToAccessoryMode();
		await fixture.Manager.PollAsync(CancellationToken.None);
		using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

		await fixture.Manager.ShutdownAsync(stop.Token).WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(fixture.Sessions.Single().StopRequested, Is.True);
			Assert.That(fixture.Sessions.Single().Ended, Is.False);
		});
	}

	[Test]
	public async Task Only_a_device_adb_actually_serves_counts_as_known_to_adb()
	{
		var adb = new FakeAdbManager
		{
			Devices =
			[
				new AdbDevice("EXAMPLE0001", AdbDeviceState.Unauthorized, null, null, null, "1", null, DateTimeOffset.UtcNow),
				new AdbDevice("EXAMPLE0002", AdbDeviceState.Device, null, null, null, "2", null, DateTimeOffset.UtcNow)
			]
		};
		var fixture = Fixture(PublicEndpointSet.HttpOnly(8193), adb: adb);
		fixture.Usb.Plug(1, [_mtp]);
		fixture.Usb.Plug(1, [_mtp], serial: "EXAMPLE0002", plug: new UsbPlugKey(1, "5"));

		await fixture.Manager.PollAsync(CancellationToken.None);
		var devices = fixture.Manager.Status.Devices.ToDictionary(device => device.Serial!);

		Assert.Multiple(() =>
		{
			Assert.That(devices["EXAMPLE0001"].KnownToAdb, Is.False);
			Assert.That(devices["EXAMPLE0001"].State, Is.EqualTo(NativeUsbDeviceState.Available));
			Assert.That(devices["EXAMPLE0002"].KnownToAdb, Is.True);
			Assert.That(devices["EXAMPLE0002"].State, Is.EqualTo(NativeUsbDeviceState.ServedByAdb));
		});
	}

	[Test]
	public async Task A_remembered_name_is_cut_without_splitting_a_character()
	{
		var store = new MemoryPreferences();
		var name = new string('a', AppPreferenceService.MaxNativeUsbDeviceNameLength - 1) + "\U0001F600" + "tail";

		await Preferences(store).SetNativeUsb(null, [new RememberedUsbDevice("EXAMPLE0001", name)]);
		var stored = (await Preferences(store).GetNativeUsb()).RememberedDevices.Single().Name!;

		Assert.That(stored, Is.EqualTo(new string('a', AppPreferenceService.MaxNativeUsbDeviceNameLength - 1) + "\U0001F600"));
	}

	[Test]
	public async Task A_corrupt_stored_list_reads_as_nothing_remembered()
	{
		var store = new MemoryPreferences { Values = { [AppPreferenceService.NativeUsbDevicesKey] = "{not json" } };

		Assert.That((await Preferences(store).GetNativeUsb()).RememberedSerials, Is.Empty);
	}

	[Test]
	public async Task A_phone_that_links_is_remembered_and_can_be_forgotten()
	{
		var fixture = Fixture(PublicEndpointSet.HttpOnly(8193));
		fixture.Usb.Plug(1, [_mtp]);
		await fixture.Manager.PollAsync(CancellationToken.None);
		await fixture.Manager.PickAsync(fixture.Manager.Status.Devices.Single().Id, CancellationToken.None);
		fixture.Usb.SwitchToAccessoryMode();
		await fixture.Manager.PollAsync(CancellationToken.None);
		fixture.Sessions.Single().Link();

		await fixture.Manager.PollAsync(CancellationToken.None);
		var remembered = (await Preferences(fixture.Store).GetNativeUsb()).RememberedDevices;
		var forgotten = await fixture.Manager.ForgetAsync("EXAMPLE0001", CancellationToken.None);

		Assert.Multiple(async () =>
		{
			Assert.That(remembered, Is.EqualTo(new[] { new RememberedUsbDevice("EXAMPLE0001", "Example Phone") }));
			Assert.That(forgotten, Is.True);
			Assert.That(fixture.Manager.Status.RememberedDevices, Is.Empty);
			Assert.That((await Preferences(fixture.Store).GetNativeUsb()).RememberedSerials, Is.Empty);
		});
	}

	[Test]
	public async Task An_https_only_host_reports_the_bridge_unavailable_and_switches_nothing()
	{
		var fixture = Fixture(PublicEndpointSet.HttpsReplacingHttp(8194));
		await Preferences(fixture.Store).SetNativeUsb(true, Remember("EXAMPLE0001"));
		fixture.Usb.Plug(1, [_mtp]);

		await fixture.Manager.PollAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(fixture.Manager.Status.BridgeAvailable, Is.False);
			Assert.That(fixture.Manager.Status.HttpsOnly, Is.True);
			Assert.That(fixture.Usb.Switched, Is.Empty);
		});
	}

	[Test]
	public async Task Turning_it_off_stops_links_and_lists_no_devices()
	{
		var fixture = Fixture(PublicEndpointSet.HttpOnly(8193));
		await Preferences(fixture.Store).SetNativeUsb(true, Remember("EXAMPLE0001"));
		fixture.Usb.SwitchToAccessoryMode();
		await fixture.Manager.PollAsync(CancellationToken.None);
		var handler = new UpdateNativeUsbSettingsRequestMessageHandler(fixture.Manager);

		var response = await handler.Handle(new UpdateNativeUsbSettingsRequest { Enabled = false }, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Enabled, Is.False);
			Assert.That(response.Devices, Is.Empty);
			Assert.That(fixture.Sessions.Single().Ended, Is.True);
		});
	}

	[Test]
	public async Task Connecting_a_device_the_host_does_not_know_reports_the_reason()
	{
		var fixture = Fixture(PublicEndpointSet.HttpOnly(8193));
		var handler = new ConnectNativeUsbDeviceRequestMessageHandler(fixture.Manager);

		var response = await handler.Handle(new ConnectNativeUsbDeviceRequest { Id = "android-9-9" }, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.ErrorCode, Is.EqualTo("UnknownDevice"));
		});
	}

	[Test]
	public void The_state_changed_event_carries_no_serial_or_device_detail()
	{
		var properties = typeof(NativeUsbStateChangedEvent).GetProperties(BindingFlags.Public | BindingFlags.Instance);

		Assert.That(properties.Select(property => property.Name), Is.EqualTo(new[] { "ChangedAt" }));
	}

	private static List<RememberedUsbDevice> Remember(params string[] serials)
		=> serials.Select(serial => new RememberedUsbDevice(serial, null)).ToList();

	private static AppPreferenceService Preferences(IAppPreferenceRepository store)
		=> new(store, new TestBuildEnvironment(), new FakeHostListenerState());

	private static (NativeUsbManager Manager, FakeUsb Usb, List<FakeLinkSession> Sessions, MemoryPreferences Store,
		UnreachableUsbmux Usbmux) Fixture(PublicEndpointSet endpoints,
		bool enabled = true,
		bool sessionsEndWhenStopped = true,
		FakeAdbManager? adb = null)
	{
		var store = new MemoryPreferences { Values = { [AppPreferenceService.NativeUsbEnabledKey] = enabled.ToString() } };
		var services = new ServiceCollection()
			.AddScoped<IAppPreferenceService>(_ => Preferences(store))
			.BuildServiceProvider();
		var usb = new FakeUsb();
		var sessions = new List<FakeLinkSession>();
		var android = new AccessoryCoordinator(usb,
			(_, _) =>
			{
				var session = new FakeLinkSession { EndsWhenStopped = sessionsEndWhenStopped };
				sessions.Add(session);
				return session;
			},
			AccessoryReconnectPolicy.Default,
			AccessoryProtocol.IdentificationStrings("Example-Mac"),
			Logger.None);
		var usbmux = new UnreachableUsbmux();
		var ios = new UsbmuxCoordinator(usbmux, (_, _) => new FakeLinkSession(), Logger.None);
		var manager = new NativeUsbManager(services.GetRequiredService<IServiceScopeFactory>(),
			adb ?? new FakeAdbManager(),
			new FakeHostListenerState { PublicEndpoints = endpoints },
			android,
			ios,
			new FakeTimeProvider());
		return (manager, usb, sessions, store, usbmux);
	}

	private sealed class UnreachableUsbmux : IUsbmuxConnector
	{
		private int _listenCalls;

		public int ListenCalls => Volatile.Read(ref _listenCalls);

		public Task ListenAsync(Action onListening, Action<UsbmuxDeviceEvent> onEvent, CancellationToken cancellationToken)
		{
			Interlocked.Increment(ref _listenCalls);
			return Task.FromException(new IOException("usbmuxd is not running in tests."));
		}

		public Task<ILinkCarrier?> ConnectAsync(int deviceId, ushort port, CancellationToken cancellationToken)
			=> Task.FromResult<ILinkCarrier?>(null);
	}
}
