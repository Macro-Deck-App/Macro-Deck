using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using MacroDeckHost.Application.Network.Tls;

namespace MacroDeckHost.Infrastructure.Network;

public sealed class LocalAddressProvider : ILocalAddressProvider
{
	public IReadOnlyList<IPAddress> GetReachableIpv4Addresses()
	{
		return NetworkInterface.GetAllNetworkInterfaces()
			.Where(nic =>
				nic.OperationalStatus == OperationalStatus.Up &&
				nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
			.SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
			.Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork &&
				!IPAddress.IsLoopback(address.Address))
			.Select(address => address.Address)
			.Distinct()
			.ToList();
	}
}
