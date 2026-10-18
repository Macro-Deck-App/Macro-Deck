using System.Net;
using System.Net.Sockets;

namespace MacroDeckHost.Integrations.Feeds.Protocol;

internal static class FeedAddressGuard
{
	private static readonly IPNetwork[] _blockedV4 =
	[
		IPNetwork.Parse("0.0.0.0/8"),
		IPNetwork.Parse("10.0.0.0/8"),
		IPNetwork.Parse("100.64.0.0/10"),
		IPNetwork.Parse("127.0.0.0/8"),
		IPNetwork.Parse("169.254.0.0/16"),
		IPNetwork.Parse("172.16.0.0/12"),
		IPNetwork.Parse("192.0.0.0/24"),
		IPNetwork.Parse("192.168.0.0/16"),
		IPNetwork.Parse("198.18.0.0/15"),
		IPNetwork.Parse("224.0.0.0/4"),
		IPNetwork.Parse("240.0.0.0/4")
	];

	private static readonly IPNetwork[] _blockedV6 =
	[
		IPNetwork.Parse("::/128"),
		IPNetwork.Parse("::1/128"),
		IPNetwork.Parse("fe80::/10"),
		IPNetwork.Parse("fec0::/10"),
		IPNetwork.Parse("fc00::/7"),
		IPNetwork.Parse("ff00::/8")
	];

	private static readonly IPNetwork _nat64 = IPNetwork.Parse("64:ff9b::/96");

	private static readonly IPNetwork _ipv4Compatible = IPNetwork.Parse("::/96");

	public static bool IsBlocked(IPAddress address)
	{
		ArgumentNullException.ThrowIfNull(address);

		if (address.IsIPv4MappedToIPv6)
		{
			return IsBlocked(address.MapToIPv4());
		}

		if (address.AddressFamily == AddressFamily.InterNetwork)
		{
			return _blockedV4.Any(network => network.Contains(address));
		}

		if (address.AddressFamily != AddressFamily.InterNetworkV6)
		{
			return true;
		}

		if (_blockedV6.Any(network => network.Contains(address)))
		{
			return true;
		}

		// An IPv6 address that only wraps an IPv4 one reaches whatever that IPv4 address is.
		return (_nat64.Contains(address) || _ipv4Compatible.Contains(address)) && IsBlocked(EmbeddedIPv4(address));
	}

	public static bool IsAllowed(IReadOnlyList<IPAddress> addresses, bool allowLocalNetwork)
	{
		ArgumentNullException.ThrowIfNull(addresses);

		return addresses.Count > 0 && (allowLocalNetwork || addresses.All(address => !IsBlocked(address)));
	}

	private static IPAddress EmbeddedIPv4(IPAddress address)
	{
		var bytes = address.GetAddressBytes();
		return new IPAddress(bytes.AsSpan(12, 4));
	}
}
