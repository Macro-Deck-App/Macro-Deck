using System.Net;
using System.Net.Sockets;
using MacroDeckHost.Integrations.WakeOnLan.Protocol;

namespace MacroDeckHost.Tests.UnitTests.WakeOnLan;

[TestFixture]
internal sealed class WakeOnLanPacketTests
{
	private static readonly byte[] _mac = [0x00, 0x11, 0x22, 0x33, 0x44, 0x55];

	[Test]
	public void A_magic_packet_is_six_ff_bytes_followed_by_the_mac_address_sixteen_times()
	{
		var packet = MagicPacket.Build(_mac);

		Assert.Multiple(() =>
		{
			Assert.That(packet, Has.Length.EqualTo(102));
			Assert.That(packet.Take(6), Is.All.EqualTo(0xFF));
			Assert.That(packet.Skip(6).Chunk(6), Has.All.EqualTo(_mac).And.Exactly(16).Items);
		});
	}

	[Test]
	public async Task The_udp_sender_delivers_the_packet_to_the_target_port()
	{
		using var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
		var target = (IPEndPoint)receiver.Client.LocalEndPoint!;
		var packet = MagicPacket.Build(_mac);
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

		await new UdpWakeOnLanSender().SendAsync(packet, target, timeout.Token);
		var received = await receiver.ReceiveAsync(timeout.Token);

		Assert.That(received.Buffer, Is.EqualTo(packet));
	}
}
