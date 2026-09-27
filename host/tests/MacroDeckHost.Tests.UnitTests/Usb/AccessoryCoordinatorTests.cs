using System.Text;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;
using MacroDeckHost.Application.Usb;
using MacroDeckHost.Infrastructure.Usb.Native;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Usb;

public class AccessoryCoordinatorTests
{
	private static readonly DateTimeOffset _start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
	private static readonly UsbPlugKey _plug = new(1, "4.2");
	private static readonly UsbInterfaceInfo _mtp = new(0, 0x06, 0x01, 0x01, 0);
	private static readonly UsbInterfaceInfo _adb = new(1, 0xFF, 0x42, 0x01, 0);
	private static readonly UsbInterfaceInfo _vendorMtp = new(0, 0xFF, 0xFF, 0x00, 5);
	private static readonly UsbInterfaceInfo _hid = new(0, 0x03, 0x00, 0x00, 0);

	private FakeUsb _usb = null!;
	private List<FakeLinkSession> _sessions = null!;
	private List<string> _sessionKeys = null!;
	private bool _sessionsEndWhenStopped = true;
	private AccessoryCoordinator _coordinator = null!;
	private DateTimeOffset _now;

	[SetUp]
	public void CreateCoordinator()
	{
		_usb = new FakeUsb();
		_sessions = [];
		_sessionKeys = [];
		_now = _start;
		_coordinator = new AccessoryCoordinator(_usb,
			(_, deviceKey) =>
			{
				var session = new FakeLinkSession { EndsWhenStopped = _sessionsEndWhenStopped };
				_sessions.Add(session);
				_sessionKeys.Add(deviceKey);
				return session;
			},
			AccessoryReconnectPolicy.Default,
			AccessoryProtocol.IdentificationStrings("Example-Mac"),
			Logger.None);
	}

	[Test]
	public void Android_candidates_are_recognised_from_descriptors_and_strings_alone()
	{
		var phone = new UsbDeviceInfo(_plug, 0x1234, 1, 0x00, 0);
		var noStrings = new Dictionary<byte, string>();

		Assert.Multiple(() =>
		{
			Assert.That(AndroidCandidateFilter.ClassifyDescriptors(phone, [_mtp, _adb]), Is.Not.Null);
			Assert.That(AndroidCandidateFilter.ClassifyDescriptors(phone, []), Is.Not.Null);
			Assert.That(AndroidCandidateFilter.ClassifyDescriptors(phone, [_adb]), Is.Not.Null);
			Assert.That(AndroidCandidateFilter.ClassifyDescriptors(phone, [_hid]), Is.Null);
			Assert.That(AndroidCandidateFilter.ClassifyDescriptors(phone with { VendorId = 0x05AC }, [_mtp]), Is.Null);
			Assert.That(AndroidCandidateFilter.ClassifyDescriptors(phone with { DeviceClass = 0x02 }, [_mtp]), Is.Null);
			Assert.That(AndroidCandidateFilter.ClassifyDescriptors(phone with { DeviceClass = 0xEF }, [_mtp]), Is.Not.Null);

			var vendorMtp = AndroidCandidateFilter.ClassifyDescriptors(phone, [_vendorMtp])!;
			Assert.That(AndroidCandidateFilter.AcceptStrings(vendorMtp,
				new UsbDeviceStrings(null, "Phone", null, new Dictionary<byte, string> { [5] = "MTP" })), Is.True);
			Assert.That(AndroidCandidateFilter.AcceptStrings(vendorMtp,
				new UsbDeviceStrings(null, "Phone", null, new Dictionary<byte, string> { [5] = "Other" })), Is.False);

			var plain = AndroidCandidateFilter.ClassifyDescriptors(phone, [_mtp, _adb])!;
			Assert.That(AndroidCandidateFilter.AcceptStrings(plain, new UsbDeviceStrings(null, "Car_Thing", null, noStrings)),
				Is.False);
			Assert.That(AndroidCandidateFilter.AcceptStrings(plain, new UsbDeviceStrings(null, "superbird", null, noStrings)),
				Is.False);
		});
	}

	[Test]
	public void A_candidate_nobody_picked_or_remembered_is_listed_but_never_switched()
	{
		_usb.Plug(1, [_mtp, _adb]);

		for (var poll = 0; poll < 5; poll++)
		{
			Poll();
		}

		Assert.Multiple(() =>
		{
			Assert.That(_usb.Switched, Is.Empty);
			Assert.That(_coordinator.Devices.Single().State, Is.EqualTo(NativeUsbDeviceState.Available));
			Assert.That(_coordinator.Devices.Single().Product, Is.EqualTo("Example Phone"));
			Assert.That(_usb.StringReads, Is.EqualTo(1));
		});
	}

	[Test]
	public void A_picked_phone_is_switched_linked_and_then_remembered()
	{
		_usb.Plug(1, [_mtp]);
		Poll();

		var pick = _coordinator.Pick(_coordinator.Devices.Single().Id);
		Poll();
		_usb.SwitchToAccessoryMode();
		Poll();
		_sessions.Single().Link();
		var linked = Poll();

		Assert.Multiple(() =>
		{
			Assert.That(pick, Is.EqualTo(NativeUsbPickResult.Picked));
			Assert.That(_usb.Switched, Is.EqualTo(new[] { _plug }));
			Assert.That(_sessionKeys.Single(), Is.EqualTo("usb:EXAMPLE0001"));
			Assert.That(linked, Is.EqualTo(new[] { new RememberedUsbDevice("EXAMPLE0001", "Example Phone") }));
			Assert.That(_coordinator.Devices.Single().State, Is.EqualTo(NativeUsbDeviceState.Linked));
		});
	}

	[Test]
	public void The_switch_identifies_the_accessory_with_this_computers_name_as_its_description()
	{
		_usb.Plug(1, [_mtp]);

		Poll(remembered: ["EXAMPLE0001"]);

		Assert.That(_usb.SentIdentifications.Single(), Is.EqualTo(new[]
		{
			"Macro Deck", "Macro Deck Companion", "Example-Mac", "1", "https://macro-deck.app", ""
		}));
	}

	[TestCase(null, "Macro Deck")]
	[TestCase("", "Macro Deck")]
	[TestCase("   ", "Macro Deck")]
	[TestCase("  MacBook-Pro-von-Manuel ", "MacBook-Pro-von-Manuel")]
	public void The_accessory_description_is_the_trimmed_computer_name_or_macro_deck(string? name, string expected)
	{
		var strings = AccessoryProtocol.IdentificationStrings(name);

		Assert.Multiple(() =>
		{
			Assert.That(strings[2], Is.EqualTo(expected));
			Assert.That(strings[0], Is.EqualTo("Macro Deck"));
			Assert.That(strings[1], Is.EqualTo("Macro Deck Companion"));
		});
	}

	[Test]
	public void A_long_computer_name_is_cut_to_64_characters_without_splitting_a_character()
	{
		var name = new string('a', 63) + "\U0001F600" + "tail";

		var description = AccessoryProtocol.IdentificationStrings(name)[2];

		Assert.Multiple(() =>
		{
			Assert.That(description, Is.EqualTo(new string('a', 63) + "\U0001F600"));
			Assert.That(char.IsHighSurrogate(description[^1]), Is.False);
			Assert.That(AccessoryProtocol.IdentificationStrings(new string('b', 100))[2], Has.Length.EqualTo(64));
		});
	}

	[Test]
	public void A_remembered_phone_is_switched_without_a_pick()
	{
		_usb.Plug(1, [_mtp]);

		Poll(remembered: ["EXAMPLE0001"]);

		Assert.That(_usb.Switched, Has.Count.EqualTo(1));
	}

	[Test]
	public void Nothing_is_switched_while_the_host_has_no_plain_http_listener_to_bridge_to()
	{
		_usb.Plug(1, [_mtp]);

		Poll(remembered: ["EXAMPLE0001"], bridge: false);
		_usb.SwitchToAccessoryMode();
		Poll(remembered: ["EXAMPLE0001"], bridge: false);

		Assert.Multiple(() =>
		{
			Assert.That(_usb.Switched, Is.Empty);
			Assert.That(_sessions, Is.Empty);
		});
	}

	[Test]
	public void A_device_adb_knows_is_left_to_adb_until_it_is_picked_and_then_switched()
	{
		_usb.Plug(1, [_adb]);

		Poll(adbSerials: ["EXAMPLE0001"]);
		Poll(adbSerials: ["EXAMPLE0001"]);
		var unpicked = _coordinator.Devices.Single();
		var switchesBeforePick = _usb.Switched.Count;
		var pick = _coordinator.Pick(unpicked.Id);
		Poll(adbSerials: ["EXAMPLE0001"]);

		Assert.Multiple(() =>
		{
			Assert.That(unpicked.State, Is.EqualTo(NativeUsbDeviceState.ServedByAdb));
			Assert.That(unpicked.KnownToAdb, Is.True);
			Assert.That(switchesBeforePick, Is.Zero);
			Assert.That(pick, Is.EqualTo(NativeUsbPickResult.Picked));
			Assert.That(_usb.Switched, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public void A_remembered_device_adb_knows_is_switched_at_once_even_with_host_adb_on()
	{
		_usb.Plug(1, [_mtp, _adb]);

		Poll(remembered: ["EXAMPLE0001"], adbSerials: ["EXAMPLE0001"]);

		Assert.That(_usb.Switched, Has.Count.EqualTo(1));
	}

	[Test]
	public void An_adb_only_phone_is_listed_while_host_adb_is_on_and_a_car_thing_never_is()
	{
		_usb.Plug(1, [_adb]);
		_usb.Plug(1, [_adb], product: "Car_Thing", serial: "CARTHING01", plug: new UsbPlugKey(1, "5"));

		Poll(adbSerials: ["EXAMPLE0001", "CARTHING01"]);

		Assert.That(_coordinator.Devices.Select(device => device.Serial), Is.EqualTo(new[] { "EXAMPLE0001" }));
	}

	[Test]
	public void A_switched_phone_that_stays_in_normal_mode_is_no_longer_native_and_adb_reverse_resumes()
	{
		var paths = new MacroDeckHost.Tests.UnitTests.TestSupport.TestPaths();
		var runner = new MacroDeckHost.Tests.UnitTests.Adb.FakeAdbProcessRunner();
		var tunnels = new MacroDeckHost.Infrastructure.Adb.AdbTunnelCoordinator(runner,
			new FakeHostListenerState(),
			new MacroDeckHost.Infrastructure.Adb.AdbOwnershipMarker(paths, Logger.None),
			Logger.None,
			_coordinator);
		tunnels.SetExecutablePath("fake-adb");
		var adbDevice = new MacroDeckHost.Application.Adb.AdbDevice("EXAMPLE0001",
			MacroDeckHost.Application.Adb.AdbDeviceState.Device, "Phone", "Example", "phone", "1", null, _now);
		_usb.Plug(1, [_adb]);

		Poll(remembered: ["EXAMPLE0001"]);
		var nativeWhileSwitching = _coordinator.IsNative("EXAMPLE0001");
		var duringSwitch = tunnels.ReconcileAsync([adbDevice], CancellationToken.None).GetAwaiter().GetResult();
		Poll(remembered: ["EXAMPLE0001"]);
		var afterFallback = tunnels.ReconcileAsync([adbDevice], CancellationToken.None).GetAwaiter().GetResult();

		Assert.Multiple(() =>
		{
			Assert.That(nativeWhileSwitching, Is.True);
			Assert.That(duringSwitch.ContainsKey("EXAMPLE0001"), Is.False);
			Assert.That(_coordinator.IsNative("EXAMPLE0001"), Is.False);
			Assert.That(afterFallback["EXAMPLE0001"].Established, Is.True);
		});
		paths.Cleanup();
	}

	[Test]
	public void A_switched_or_linked_device_is_reported_as_native_until_it_is_unplugged()
	{
		_usb.Plug(1, [_adb]);
		Poll(remembered: ["EXAMPLE0001"]);
		var whileSwitching = _coordinator.IsNative("EXAMPLE0001");
		_usb.SwitchToAccessoryMode();
		Poll(remembered: ["EXAMPLE0001"]);
		var inAccessoryMode = _coordinator.IsNative("EXAMPLE0001");
		_usb.Unplug();
		Poll(remembered: ["EXAMPLE0001"]);
		Poll(remembered: ["EXAMPLE0001"]);

		Assert.Multiple(() =>
		{
			Assert.That(whileSwitching, Is.True);
			Assert.That(inAccessoryMode, Is.True);
			Assert.That(_coordinator.IsNative("EXAMPLE0001"), Is.False);
			Assert.That(_coordinator.IsNative("OTHER"), Is.False);
		});
	}

	[Test]
	public void A_phone_switched_but_never_linked_is_not_switched_again_until_it_is_unplugged()
	{
		_usb.Plug(1, [_mtp]);
		Poll(remembered: ["EXAMPLE0001"]);
		_usb.SwitchToAccessoryMode();
		Poll(remembered: ["EXAMPLE0001"]);
		_usb.Plug(1, [_mtp]);

		for (var poll = 0; poll < 60; poll++)
		{
			Advance(TimeSpan.FromSeconds(3));
			Poll(remembered: ["EXAMPLE0001"]);
		}

		var switchesBeforeReplug = _usb.Switched.Count;
		var stateBeforeReplug = _coordinator.Devices.Single().State;
		_usb.Unplug();
		Poll(remembered: ["EXAMPLE0001"]);
		Poll(remembered: ["EXAMPLE0001"]);
		_usb.Plug(1, [_mtp]);
		Poll(remembered: ["EXAMPLE0001"]);

		Assert.Multiple(() =>
		{
			Assert.That(switchesBeforeReplug, Is.EqualTo(1));
			Assert.That(stateBeforeReplug, Is.EqualTo(NativeUsbDeviceState.Stopped));
			Assert.That(_usb.Switched, Has.Count.EqualTo(2));
		});
	}

	[Test]
	public void A_phone_being_switched_shows_switching_offers_no_connect_and_a_second_pick_does_not_switch_again()
	{
		_usb.Plug(1, [_mtp]);
		Poll();
		var id = _coordinator.Devices.Single().Id;
		_coordinator.Pick(id);
		Poll();
		var switching = _coordinator.Devices.Single();
		var secondPick = _coordinator.Pick(id);
		_usb.Unplug();
		Poll();
		var whileAway = _coordinator.Devices.Single();

		Assert.Multiple(() =>
		{
			Assert.That(switching.State, Is.EqualTo(NativeUsbDeviceState.Switching));
			Assert.That(NativeUsbDeviceDto.From(switching).CanConnect, Is.False);
			Assert.That(secondPick, Is.EqualTo(NativeUsbPickResult.Picked));
			Assert.That(whileAway.State, Is.EqualTo(NativeUsbDeviceState.Switching));
			Assert.That(_usb.Switched, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public void A_pick_cannot_override_the_app_closing_the_link_until_the_phone_is_unplugged()
	{
		_usb.Plug(1, [_mtp]);
		Poll(remembered: ["EXAMPLE0001"]);
		_usb.SwitchToAccessoryMode();
		Poll(remembered: ["EXAMPLE0001"]);
		var session = _sessions.Single();
		session.Link();
		Poll(remembered: ["EXAMPLE0001"]);
		session.ByeReceived = true;
		session.IsLinked = false;
		session.End();
		_usb.Plug(1, [_mtp]);
		Poll(remembered: ["EXAMPLE0001"]);

		var pick = _coordinator.Pick(_coordinator.Devices.Single().Id);
		Poll(remembered: ["EXAMPLE0001"]);

		Assert.Multiple(() =>
		{
			Assert.That(pick, Is.EqualTo(NativeUsbPickResult.NotAllowed));
			Assert.That(_usb.Switched, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public void The_description_stays_within_androids_accessory_string_limit_without_splitting_a_character()
	{
		var emoji = "\U0001F600";
		var description = AccessoryProtocol.IdentificationStrings(string.Concat(Enumerable.Repeat(emoji, 60)))[2];
		var wide = AccessoryProtocol.IdentificationStrings(new string('\u65E5', 70))[2];

		Assert.Multiple(() =>
		{
			Assert.That(Encoding.UTF8.GetByteCount(description), Is.LessThanOrEqualTo(AccessoryProtocol.MaxDescriptionBytes));
			Assert.That(description, Is.EqualTo(string.Concat(Enumerable.Repeat(emoji, 50))));
			Assert.That(wide, Is.EqualTo(new string('\u65E5', 64)));
		});
	}

	[Test]
	public void An_explicit_pick_switches_a_never_linked_phone_again_without_a_replug()
	{
		_usb.Plug(1, [_mtp]);
		Poll();
		_coordinator.Pick(_coordinator.Devices.Single().Id);
		Poll();
		_usb.SwitchToAccessoryMode();
		Poll();
		_usb.Plug(1, [_mtp]);
		Poll();
		Advance(TimeSpan.FromMinutes(5));
		Poll();
		var stopped = _coordinator.Devices.Single();
		var automaticSwitches = _usb.Switched.Count;

		var pick = _coordinator.Pick(stopped.Id);
		Poll();

		Assert.Multiple(() =>
		{
			Assert.That(stopped.State, Is.EqualTo(NativeUsbDeviceState.Stopped));
			Assert.That(automaticSwitches, Is.EqualTo(1));
			Assert.That(pick, Is.EqualTo(NativeUsbPickResult.Picked));
			Assert.That(_usb.Switched, Has.Count.EqualTo(2));
		});
	}

	[Test]
	public void After_a_loss_without_bye_the_phone_is_switched_again_after_5_s_30_s_and_2_min_and_then_no_more()
	{
		_usb.Plug(1, [_mtp]);
		Poll(remembered: ["EXAMPLE0001"]);
		var waits = new List<TimeSpan>();

		for (var loss = 0; loss < 4; loss++)
		{
			_usb.SwitchToAccessoryMode();
			Poll(remembered: ["EXAMPLE0001"]);
			_sessions.Last().Link();
			Poll(remembered: ["EXAMPLE0001"]);
			_sessions.Last().End();
			_usb.Plug(1, [_mtp]);
			var lostAt = _now;
			var switches = _usb.Switched.Count;
			Poll(remembered: ["EXAMPLE0001"]);
			while (_usb.Switched.Count == switches && _now - lostAt < TimeSpan.FromMinutes(10))
			{
				Advance(TimeSpan.FromSeconds(1));
				Poll(remembered: ["EXAMPLE0001"]);
			}

			waits.Add(_now - lostAt);
		}

		Assert.Multiple(() =>
		{
			Assert.That(waits.Take(3), Is.EqualTo(new[]
			{
				TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2)
			}));
			Assert.That(waits[3], Is.GreaterThanOrEqualTo(TimeSpan.FromMinutes(10)));
			Assert.That(_usb.Switched, Has.Count.EqualTo(4));
		});
	}

	[Test]
	public void After_the_app_says_bye_the_phone_is_not_switched_again_until_it_is_unplugged()
	{
		_usb.Plug(1, [_mtp]);
		Poll(remembered: ["EXAMPLE0001"]);
		_usb.SwitchToAccessoryMode();
		Poll(remembered: ["EXAMPLE0001"]);
		var session = _sessions.Single();
		session.Link();
		Poll(remembered: ["EXAMPLE0001"]);
		session.ByeReceived = true;
		session.IsLinked = false;
		session.End();
		_usb.Plug(1, [_mtp]);

		for (var poll = 0; poll < 100; poll++)
		{
			Advance(TimeSpan.FromSeconds(3));
			Poll(remembered: ["EXAMPLE0001"]);
		}

		Assert.Multiple(() =>
		{
			Assert.That(_usb.Switched, Has.Count.EqualTo(1));
			Assert.That(_coordinator.Devices.Single().State, Is.EqualTo(NativeUsbDeviceState.Closed));
		});
	}

	[Test]
	public void A_link_lost_while_the_phone_stays_in_accessory_mode_reopens_the_handle_without_switching()
	{
		_usb.Plug(1, [_mtp]);
		Poll(remembered: ["EXAMPLE0001"]);
		_usb.SwitchToAccessoryMode();
		Poll(remembered: ["EXAMPLE0001"]);
		_sessions.Single().Link();
		Poll(remembered: ["EXAMPLE0001"]);

		_sessions.Single().End();
		Poll(remembered: ["EXAMPLE0001"]);

		Assert.Multiple(() =>
		{
			Assert.That(_sessions, Has.Count.EqualTo(2));
			Assert.That(_usb.Switched, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public void A_link_that_never_heard_the_app_is_reopened_only_after_a_backoff()
	{
		_usb.Plug(1, [_mtp]);
		Poll(remembered: ["EXAMPLE0001"]);
		_usb.SwitchToAccessoryMode();
		Poll(remembered: ["EXAMPLE0001"]);
		_sessions.Single().End();

		Poll(remembered: ["EXAMPLE0001"]);
		Advance(AccessoryCoordinator.ReopenBackoff - TimeSpan.FromSeconds(1));
		Poll(remembered: ["EXAMPLE0001"]);
		var beforeBackoff = _sessions.Count;
		Advance(TimeSpan.FromSeconds(1));
		Poll(remembered: ["EXAMPLE0001"]);

		Assert.Multiple(() =>
		{
			Assert.That(beforeBackoff, Is.EqualTo(1));
			Assert.That(_sessions, Has.Count.EqualTo(2));
			Assert.That(_coordinator.Devices.Single().State, Is.EqualTo(NativeUsbDeviceState.WaitingForApp));
		});
	}

	[Test]
	public async Task Sessions_stopped_when_the_feature_turns_off_are_still_awaited()
	{
		_sessionsEndWhenStopped = false;
		_usb.SwitchToAccessoryMode();
		Poll(remembered: ["EXAMPLE0001"]);

		_coordinator.StopAll();
		var stopped = _coordinator.WhenStopped();
		var pendingBeforeEnd = stopped.IsCompleted;
		_sessions.Single().End();
		await stopped.WaitAsync(TimeSpan.FromSeconds(10));

		Assert.Multiple(() =>
		{
			Assert.That(pendingBeforeEnd, Is.False);
			Assert.That(_sessions.Single().Ended, Is.True);
		});
	}

	[Test]
	public void An_accessory_mode_phone_nobody_switched_picked_or_remembered_is_not_linked()
	{
		_usb.SwitchToAccessoryMode();

		Poll();

		Assert.Multiple(() =>
		{
			Assert.That(_sessions, Is.Empty);
			Assert.That(_coordinator.Devices.Single().State, Is.EqualTo(NativeUsbDeviceState.Available));
		});
	}

	[Test]
	public void A_phone_without_accessory_support_is_marked_and_never_retried_on_that_plug()
	{
		_usb.SwitchOutcome = AccessorySwitchOutcome.NotSupported;
		_usb.Plug(1, [_mtp]);

		Poll(remembered: ["EXAMPLE0001"]);
		Poll(remembered: ["EXAMPLE0001"]);

		Assert.Multiple(() =>
		{
			Assert.That(_usb.Switched, Has.Count.EqualTo(1));
			Assert.That(_coordinator.Devices.Single().State, Is.EqualTo(NativeUsbDeviceState.NotSupported));
			Assert.That(_coordinator.Pick(_coordinator.Devices.Single().Id), Is.EqualTo(NativeUsbPickResult.NotAllowed));
			Assert.That(NativeUsbDeviceDto.From(_coordinator.Devices.Single()).CanConnect, Is.False);
		});
	}

	[Test]
	public void The_same_phone_reappearing_after_one_absent_poll_keeps_its_pick()
	{
		_usb.Plug(1, [_mtp]);
		Poll();
		_coordinator.Pick(_coordinator.Devices.Single().Id);
		_usb.SwitchOutcome = AccessorySwitchOutcome.Failed;
		_usb.Unplug();
		Poll();
		_usb.Plug(1, [_mtp]);
		Poll();

		Assert.Multiple(() =>
		{
			Assert.That(_usb.Switched, Has.Count.EqualTo(1));
			Assert.That(_usb.StringReads, Is.EqualTo(2));
		});
	}

	[Test]
	public void A_different_phone_on_the_same_port_within_one_poll_is_a_new_unpicked_device()
	{
		_usb.Plug(1, [_mtp]);
		Poll();
		_coordinator.Pick(_coordinator.Devices.Single().Id);
		_usb.SwitchOutcome = AccessorySwitchOutcome.NotSupported;
		Poll();
		_usb.Unplug();
		Poll();
		_usb.Plug(1, [_mtp], product: "Another Phone", serial: "OTHER0002");
		Poll();

		var device = _coordinator.Devices.Single();
		Assert.Multiple(() =>
		{
			Assert.That(_usb.Switched, Has.Count.EqualTo(1));
			Assert.That(device.Serial, Is.EqualTo("OTHER0002"));
			Assert.That(device.Picked, Is.False);
			Assert.That(device.State, Is.EqualTo(NativeUsbDeviceState.Available));
		});
	}

	[Test]
	public void A_different_phone_after_a_linked_one_does_not_inherit_its_re_switch_history()
	{
		_usb.Plug(1, [_mtp]);
		Poll(remembered: ["EXAMPLE0001"]);
		_usb.SwitchToAccessoryMode();
		Poll(remembered: ["EXAMPLE0001"]);
		_sessions.Single().Link();
		Poll(remembered: ["EXAMPLE0001"]);
		_usb.Unplug();
		Poll(remembered: ["EXAMPLE0001"]);
		_usb.Plug(1, [_mtp], serial: "OTHER0002");

		for (var poll = 0; poll < 60; poll++)
		{
			Advance(TimeSpan.FromSeconds(3));
			Poll(remembered: ["EXAMPLE0001"]);
		}

		Assert.That(_usb.Switched, Has.Count.EqualTo(1));
	}

	[Test]
	public void After_a_bye_the_accessory_is_not_reopened_while_the_phone_stays_in_accessory_mode()
	{
		_usb.Plug(1, [_mtp]);
		Poll(remembered: ["EXAMPLE0001"]);
		_usb.SwitchToAccessoryMode();
		Poll(remembered: ["EXAMPLE0001"]);
		var session = _sessions.Single();
		session.Link();
		Poll(remembered: ["EXAMPLE0001"]);
		session.ByeReceived = true;
		session.IsLinked = false;
		Poll(remembered: ["EXAMPLE0001"]);
		var stateWhileOpen = _coordinator.Devices.Single().State;
		session.End();

		for (var poll = 0; poll < 20; poll++)
		{
			Advance(TimeSpan.FromSeconds(3));
			Poll(remembered: ["EXAMPLE0001"]);
		}

		Assert.Multiple(() =>
		{
			Assert.That(stateWhileOpen, Is.EqualTo(NativeUsbDeviceState.Closed));
			Assert.That(_sessions, Has.Count.EqualTo(1));
			Assert.That(_coordinator.Devices.Single().State, Is.EqualTo(NativeUsbDeviceState.Closed));
		});
	}

	[TearDown]
	public void DisposeUsb() => _usb.Dispose();

	private IReadOnlyList<RememberedUsbDevice> Poll(IReadOnlyCollection<string>? remembered = null,
		bool bridge = true,
		IReadOnlyCollection<string>? adbSerials = null)
		=> _coordinator.Poll(new AccessoryPollContext(bridge,
				(adbSerials ?? []).ToHashSet(),
				(remembered ?? []).ToHashSet()),
			_now);

	private void Advance(TimeSpan by) => _now += by;
}
