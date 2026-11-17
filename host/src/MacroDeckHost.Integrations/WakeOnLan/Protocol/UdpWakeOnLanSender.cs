using System.Net;
using System.Net.Sockets;

namespace MacroDeckHost.Integrations.WakeOnLan.Protocol;

internal interface IWakeOnLanSender
{
	Task SendAsync(byte[] packet, IPEndPoint target, CancellationToken cancellationToken);
}

internal sealed class UdpWakeOnLanSender : IWakeOnLanSender
{
	public async Task SendAsync(byte[] packet, IPEndPoint target, CancellationToken cancellationToken)
	{
		using var client = new UdpClient(AddressFamily.InterNetwork);
		client.EnableBroadcast = true;
		await client.SendAsync(packet, target, cancellationToken).ConfigureAwait(false);
	}
}
