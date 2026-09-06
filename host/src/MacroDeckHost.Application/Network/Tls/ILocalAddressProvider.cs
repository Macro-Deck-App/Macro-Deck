using System.Net;

namespace MacroDeckHost.Application.Network.Tls;

public interface ILocalAddressProvider
{
	IReadOnlyList<IPAddress> GetReachableIpv4Addresses();
}
