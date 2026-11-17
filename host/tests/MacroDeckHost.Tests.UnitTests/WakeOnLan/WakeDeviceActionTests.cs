using System.Net;
using System.Net.Sockets;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.WakeOnLan.Actions;
using MacroDeckHost.Integrations.WakeOnLan.Configuration;
using MacroDeckHost.Integrations.WakeOnLan.Protocol;
using MacroDeckHost.Tests.UnitTests.Delegation;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.WakeOnLan;

[TestFixture]
internal sealed class WakeDeviceActionTests
{
	private static readonly byte[] _mac = [0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0x01];

	private readonly List<TimeSpan> _delays = [];
	private RecordingIntegrationConfig _config = null!;

	[SetUp]
	public void SetUp()
	{
		_delays.Clear();
		_config = new RecordingIntegrationConfig();
	}

	private Guid AddDevice(
		string broadcast = "192.168.2.255",
		string port = "7",
		string packets = "1",
		string delay = "250",
		string mac = "AA:BB:CC:DD:EE:01")
	{
		return _config.AddEntry("Gaming PC",
			new Dictionary<string, string?>
			{
				[WakeOnLanConfigKeys.MacAddress] = mac,
				[WakeOnLanConfigKeys.BroadcastAddress] = broadcast,
				[WakeOnLanConfigKeys.Port] = port,
				[WakeOnLanConfigKeys.PacketCount] = packets,
				[WakeOnLanConfigKeys.PacketDelay] = delay
			});
	}

	private WakeDeviceActionDefinition Definition(IWakeOnLanSender sender)
	{
		return new WakeDeviceActionDefinition(() => _config,
			sender,
			(delay, _) =>
			{
				_delays.Add(delay);
				return Task.CompletedTask;
			},
			Logger.None);
	}

	private Task<ActionResult> Wake(IWakeOnLanSender sender, object? device)
	{
		var parameters = new Dictionary<string, object>();
		if (device is not null)
		{
			parameters[WakeDeviceActionDefinition.DeviceParameter] = device;
		}

		return Definition(sender).CreateExecutor().ExecuteAsync(new ActionExecutionContext { Parameters = parameters });
	}

	[Test]
	public async Task Waking_a_device_sends_its_magic_packet_to_the_saved_broadcast_address_and_port()
	{
		var sender = new RecordingWakeOnLanSender();
		var device = AddDevice();

		var result = await Wake(sender, device.ToString());

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(sender.Sent, Has.Count.EqualTo(1));
			Assert.That(sender.Sent[0].Target, Is.EqualTo(new IPEndPoint(IPAddress.Parse("192.168.2.255"), 7)));
			Assert.That(sender.Sent[0].Packet, Is.EqualTo(MagicPacket.Build(_mac)));
			Assert.That(_delays, Is.Empty);
		});
	}

	[Test]
	public async Task A_device_set_to_several_packets_gets_each_one_with_the_delay_between_them()
	{
		var sender = new RecordingWakeOnLanSender();
		var device = AddDevice(packets: "3", delay: "250");

		var result = await Wake(sender, device.ToString());

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(sender.Sent, Has.Count.EqualTo(3));
			Assert.That(_delays, Is.EqualTo(new[] { TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250) }));
		});
	}

	[Test]
	public async Task A_failed_send_stops_the_remaining_packets_and_fails_the_action()
	{
		var sender = new RecordingWakeOnLanSender { FailOnSend = 2, Failure = SocketError.NetworkUnreachable };
		var device = AddDevice(packets: "3");

		var result = await Wake(sender, device.ToString());

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotConnected));
			Assert.That(sender.Sent, Has.Count.EqualTo(2));
			Assert.That(_delays, Has.Count.EqualTo(1));
		});
	}

	[TestCase(SocketError.AccessDenied, ActionErrorCodes.PermissionDenied)]
	[TestCase(SocketError.HostUnreachable, ActionErrorCodes.NotConnected)]
	[TestCase(SocketError.NetworkUnreachable, ActionErrorCodes.NotConnected)]
	[TestCase(SocketError.NetworkDown, ActionErrorCodes.NotConnected)]
	public async Task A_send_failure_is_reported_with_a_message_that_names_the_address(
		SocketError failure,
		string expectedCode)
	{
		var sender = new RecordingWakeOnLanSender { FailOnSend = 1, Failure = failure };
		var device = AddDevice();

		var result = await Wake(sender, device.ToString());

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(expectedCode));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("192.168.2.255"));
		});
	}

	[Test]
	public async Task Waking_without_a_chosen_device_asks_for_one()
	{
		var sender = new RecordingWakeOnLanSender();

		var result = await Wake(sender, null);

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotConfigured));
			Assert.That(sender.Sent, Is.Empty);
		});
	}

	[Test]
	public async Task Waking_a_deleted_device_reports_it_as_not_found()
	{
		var sender = new RecordingWakeOnLanSender();
		var device = AddDevice();
		_config.RemoveEntry(device);

		var result = await Wake(sender, device.ToString());

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
			Assert.That(sender.Sent, Is.Empty);
		});
	}

	[Test]
	public async Task A_device_with_corrupt_saved_settings_is_refused_without_sending()
	{
		var sender = new RecordingWakeOnLanSender();
		var device = AddDevice(mac: "not a mac");

		var result = await Wake(sender, device.ToString());

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("Gaming PC"));
			Assert.That(sender.Sent, Is.Empty);
		});
	}

	[Test]
	public void Cancelling_during_the_delay_between_packets_stops_the_action()
	{
		var sender = new RecordingWakeOnLanSender();
		var device = AddDevice(packets: "3", delay: "10000");
		using var cancellation = new CancellationTokenSource();
		var definition = new WakeDeviceActionDefinition(() => _config,
			sender,
			(delay, token) =>
			{
				cancellation.Cancel();
				return Task.Delay(delay, token);
			},
			Logger.None);

		Assert.That(async () => await definition.CreateExecutor()
				.ExecuteAsync(new ActionExecutionContext
				{
					Parameters = new Dictionary<string, object>
					{
						[WakeDeviceActionDefinition.DeviceParameter] = device.ToString()
					},
					CancellationToken = cancellation.Token
				}),
			Throws.InstanceOf<OperationCanceledException>());
		Assert.That(sender.Sent, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task The_device_choice_lists_every_saved_device()
	{
		var first = AddDevice();
		var second = _config.AddEntry("Office NAS", new Dictionary<string, string?>());

		var options = await Definition(new RecordingWakeOnLanSender())
			.GetDynamicOptionsAsync(new DynamicOptionsContext
				{
					ParameterName = WakeDeviceActionDefinition.DeviceParameter,
					CurrentParameters = new Dictionary<string, object?>()
				},
				CancellationToken.None);

		Assert.That(options.Options.Select(option => (option.Value, TestLocalization.Resolve(option.Label))),
			Is.EqualTo(new[] { (first.ToString(), "Gaming PC"), (second.ToString(), "Office NAS") }));
	}
}
