using System.Net;
using System.Net.Sockets;

namespace MacroDeckHost;

internal readonly record struct PublicListenerProbeResult(bool CanBind, SocketError Error, int NativeErrorCode);

internal static class PublicListenerProbe
{
	internal static PublicListenerProbeResult Probe(int port)
	{
		var ipv6Result = TryBind(AddressFamily.InterNetworkV6, IPAddress.IPv6Any, port, dualMode: true);
		if (ipv6Result.CanBind || ipv6Result.Error == SocketError.AddressAlreadyInUse)
		{
			return ipv6Result;
		}

		return TryBind(AddressFamily.InterNetwork, IPAddress.Any, port, dualMode: false);
	}

	private static PublicListenerProbeResult TryBind(AddressFamily family, IPAddress address, int port, bool dualMode)
	{
		try
		{
			using var socket = new Socket(family, SocketType.Stream, ProtocolType.Tcp);
			if (dualMode)
			{
				socket.DualMode = true;
			}

			socket.Bind(new IPEndPoint(address, port));
			return new PublicListenerProbeResult(true, SocketError.Success, 0);
		}
		catch (SocketException ex)
		{
			return new PublicListenerProbeResult(false, ex.SocketErrorCode, ex.ErrorCode);
		}
	}
}
