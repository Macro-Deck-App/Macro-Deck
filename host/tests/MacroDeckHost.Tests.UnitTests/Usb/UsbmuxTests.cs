using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using MacroDeckHost.Application.Usb;
using MacroDeckHost.Infrastructure.Usb.Native;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Usb;

public class UsbmuxTests
{
	[Test]
	public void A_property_list_round_trips_dicts_arrays_strings_integers_booleans_and_data()
	{
		var original = new Dictionary<string, object>
		{
			["MessageType"] = "Attached",
			["DeviceID"] = 7L,
			["Paired"] = true,
			["Other"] = false,
			["Blob"] = new byte[] { 1, 2, 3 },
			["Properties"] = new Dictionary<string, object> { ["SerialNumber"] = "00008150-000A1B2C3D4E5F6A" },
			["List"] = new List<object> { "a", 1L }
		};

		var parsed = UsbmuxPlist.Read(UsbmuxPlist.Write(original));

		Assert.Multiple(() =>
		{
			Assert.That(parsed["MessageType"], Is.EqualTo("Attached"));
			Assert.That(parsed["DeviceID"], Is.EqualTo(7L));
			Assert.That(parsed["Paired"], Is.EqualTo(true));
			Assert.That(parsed["Other"], Is.EqualTo(false));
			Assert.That(parsed["Blob"], Is.EqualTo(new byte[] { 1, 2, 3 }));
			Assert.That(((Dictionary<string, object>)parsed["Properties"])["SerialNumber"],
				Is.EqualTo("00008150-000A1B2C3D4E5F6A"));
			Assert.That(parsed["List"], Is.EqualTo(new List<object> { "a", 1L }));
		});
	}

	[Test]
	public void A_usbmuxd_style_document_with_a_doctype_is_read()
	{
		const string document = """
			<?xml version="1.0" encoding="UTF-8"?>
			<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
			<plist version="1.0">
			<dict>
				<key>MessageType</key>
				<string>Result</string>
				<key>Number</key>
				<integer>3</integer>
			</dict>
			</plist>
			""";

		var parsed = UsbmuxPlist.Read(Encoding.UTF8.GetBytes(document));

		Assert.That(UsbmuxProtocol.ResultNumber(parsed), Is.EqualTo(3));
	}

	[Test]
	public void A_message_has_a_little_endian_header_with_its_length_version_1_plist_type_8_and_the_tag()
	{
		var message = UsbmuxProtocol.Encode(42, UsbmuxProtocol.Listen());

		Assert.Multiple(() =>
		{
			Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(message), Is.EqualTo((uint)message.Length));
			Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(message.AsSpan(4)), Is.EqualTo(1u));
			Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(message.AsSpan(8)), Is.EqualTo(8u));
			Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(message.AsSpan(12)), Is.EqualTo(42u));
			Assert.That(UsbmuxPlist.Read(message.AsSpan(16))["MessageType"], Is.EqualTo("Listen"));
		});
	}

	[Test]
	public void Connect_asks_for_the_companion_link_port_8197_in_network_byte_order()
	{
		var connect = UsbmuxProtocol.Connect(5, UsbmuxProtocol.CompanionLinkPort);

		Assert.Multiple(() =>
		{
			Assert.That(connect["MessageType"], Is.EqualTo("Connect"));
			Assert.That(connect["DeviceID"], Is.EqualTo(5L));
			Assert.That(connect["PortNumber"], Is.EqualTo(0x0520L));
		});
	}

	[Test]
	public void Attach_events_report_whether_the_device_is_on_usb()
	{
		var usb = UsbmuxProtocol.ParseDeviceEvent(new Dictionary<string, object>
		{
			["MessageType"] = "Attached",
			["DeviceID"] = 3L,
			["Properties"] = new Dictionary<string, object> { ["ConnectionType"] = "USB", ["SerialNumber"] = "ABC" }
		});
		var network = UsbmuxProtocol.ParseDeviceEvent(new Dictionary<string, object>
		{
			["MessageType"] = "Attached",
			["DeviceID"] = 4L,
			["Properties"] = new Dictionary<string, object> { ["ConnectionType"] = "Network" }
		});
		var detached = UsbmuxProtocol.ParseDeviceEvent(new Dictionary<string, object>
		{
			["MessageType"] = "Detached",
			["DeviceID"] = 3L
		});

		Assert.Multiple(() =>
		{
			Assert.That(usb, Is.EqualTo(new UsbmuxDeviceEvent(true, 3, "ABC", true)));
			Assert.That(network!.Usb, Is.False);
			Assert.That(detached, Is.EqualTo(new UsbmuxDeviceEvent(false, 3, null, false)));
		});
	}

	[Test]
	public async Task Connect_returns_the_tunnel_socket_on_success_and_null_when_the_port_refuses()
	{
		using var usbmuxd = new TcpListener(IPAddress.Loopback, 0);
		usbmuxd.Start();
		var client = new UsbmuxClient(usbmuxd.LocalEndpoint);

		var accepted = ServeOneAsync(usbmuxd, resultNumber: 0, thenSend: "tunnel"u8.ToArray());
		using var tunnel = await client.ConnectAsync(9, UsbmuxProtocol.CompanionLinkPort, CancellationToken.None);
		var request = await accepted;
		var buffer = new byte[6];
		await tunnel!.ReceiveAsync(buffer);

		var refused = ServeOneAsync(usbmuxd, resultNumber: 3, thenSend: []);
		var none = await client.ConnectAsync(9, UsbmuxProtocol.CompanionLinkPort, CancellationToken.None);
		await refused;

		Assert.Multiple(() =>
		{
			Assert.That(request["MessageType"], Is.EqualTo("Connect"));
			Assert.That(request["PortNumber"], Is.EqualTo(0x0520L));
			Assert.That(buffer, Is.EqualTo("tunnel"u8.ToArray()));
			Assert.That(none, Is.Null);
		});
	}

	[Test]
	public async Task Usbmux_devices_get_one_link_each_retried_every_three_seconds_and_network_devices_are_ignored()
	{
		var connector = new FakeConnector();
		var sessions = new List<(string Key, FakeSession Session)>();
		using var coordinator = new UsbmuxCoordinator(connector,
			(_, key) =>
			{
				var session = new FakeSession();
				sessions.Add((key, session));
				return session;
			},
			Logger.None);
		var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

		coordinator.Poll(true, now);
		await Eventually.True(() => coordinator.Available, "the listen connection is up");
		connector.Raise(new UsbmuxDeviceEvent(true, 1, "IPHONE-UDID", true));
		connector.Raise(new UsbmuxDeviceEvent(true, 2, "OVER-WIFI", false));

		coordinator.Poll(true, now);
		await Eventually.True(() => connector.Attempts == 1, "the first connect was attempted");
		coordinator.Poll(true, now.AddSeconds(1));
		coordinator.Poll(true, now.AddSeconds(3));
		await Eventually.True(() => connector.Attempts == 2, "the connect was retried");

		connector.Accept = true;
		coordinator.Poll(true, now.AddSeconds(6));
		await Eventually.True(() => sessions.Count == 1, "the link opened");
		coordinator.Poll(true, now.AddSeconds(9));
		coordinator.Poll(true, now.AddSeconds(12));

		Assert.Multiple(() =>
		{
			Assert.That(connector.Attempts, Is.EqualTo(3));
			Assert.That(connector.AttemptedDevices.Distinct(), Is.EqualTo(new[] { 1 }));
			Assert.That(connector.Ports.Distinct(), Is.EqualTo(new[] { (ushort)8197 }));
			Assert.That(sessions.Single().Key, Is.EqualTo("usb:IPHONE-UDID"));
			Assert.That(coordinator.Devices.Single().Platform, Is.EqualTo(NativeUsbPlatform.Ios));
		});

		connector.Raise(new UsbmuxDeviceEvent(false, 1, null, false));
		Assert.That(sessions.Single().Session.Ended, Is.True);
	}

	[Test]
	public async Task Stopping_waits_for_ios_links_to_end()
	{
		var connector = new FakeConnector { Accept = true };
		var session = new FakeLinkSession { EndsWhenStopped = false };
		session.Link();
		using var coordinator = new UsbmuxCoordinator(connector, (_, _) => session, Logger.None);
		var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
		coordinator.Poll(true, now);
		await Eventually.True(() => coordinator.Available, "the listen connection is up");
		connector.Raise(new UsbmuxDeviceEvent(true, 1, "IPHONE-UDID", true));
		coordinator.Poll(true, now);
		await Eventually.True(() => coordinator.Devices.Single().State == NativeUsbDeviceState.Linked, "the link opened");

		coordinator.StopAll();
		var stopped = coordinator.WhenStopped();
		var pendingBeforeEnd = stopped.IsCompleted;
		session.End();
		await stopped.WaitAsync(TimeSpan.FromSeconds(10));

		Assert.Multiple(() =>
		{
			Assert.That(session.StopRequested, Is.True);
			Assert.That(pendingBeforeEnd, Is.False);
		});
	}

	private static async Task<Dictionary<string, object>> ServeOneAsync(TcpListener listener,
		long resultNumber,
		byte[] thenSend)
	{
		var socket = await listener.AcceptSocketAsync();
		var header = new byte[16];
		await ReceiveExactlyAsync(socket, header);
		var payload = new byte[BinaryPrimitives.ReadUInt32LittleEndian(header) - 16];
		await ReceiveExactlyAsync(socket, payload);
		var result = new Dictionary<string, object> { ["MessageType"] = "Result", ["Number"] = resultNumber };
		await socket.SendAsync(UsbmuxProtocol.Encode(BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12)), result));
		if (thenSend.Length > 0)
		{
			await socket.SendAsync(thenSend);
		}

		return UsbmuxPlist.Read(payload);
	}

	private static async Task ReceiveExactlyAsync(Socket socket, byte[] buffer)
	{
		var read = 0;
		while (read < buffer.Length)
		{
			read += await socket.ReceiveAsync(buffer.AsMemory(read));
		}
	}

	private sealed class FakeSession : INativeLinkSession
	{
		public bool IsLinked => true;

		public bool EverLinked => true;

		public bool ByeReceived => false;

		public bool Ended { get; private set; }

		public Task Completion => Task.CompletedTask;

		public void Stop() => Ended = true;
	}

	private sealed class FakeConnector : IUsbmuxConnector
	{
		private Action<UsbmuxDeviceEvent>? _onEvent;
		private int _attempts;

		public bool Accept { get; set; }

		public int Attempts => Volatile.Read(ref _attempts);

		public List<int> AttemptedDevices { get; } = [];

		public List<ushort> Ports { get; } = [];

		public void Raise(UsbmuxDeviceEvent deviceEvent) => _onEvent!(deviceEvent);

		public async Task ListenAsync(Action onListening, Action<UsbmuxDeviceEvent> onEvent, CancellationToken cancellationToken)
		{
			_onEvent = onEvent;
			onListening();
			await Task.Delay(Timeout.Infinite, cancellationToken);
		}

		public Task<ILinkCarrier?> ConnectAsync(int deviceId, ushort port, CancellationToken cancellationToken)
		{
			lock (AttemptedDevices)
			{
				AttemptedDevices.Add(deviceId);
				Ports.Add(port);
			}

			Interlocked.Increment(ref _attempts);
			return Task.FromResult<ILinkCarrier?>(Accept ? new FakeLinkCarrier() : null);
		}
	}
}
