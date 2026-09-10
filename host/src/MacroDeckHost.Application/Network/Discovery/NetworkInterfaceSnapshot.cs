using System.Net;
using System.Net.NetworkInformation;

namespace MacroDeckHost.Application.Network.Discovery;

public sealed record NetworkInterfaceSnapshot(
	int Index,
	string Name,
	string Description,
	NetworkInterfaceType Type,
	bool IsUp,
	IReadOnlyList<IPAddress> Ipv4Addresses);

public interface INetworkInterfaceSnapshotProvider
{
	IReadOnlyList<NetworkInterfaceSnapshot> GetInterfaces();
}
