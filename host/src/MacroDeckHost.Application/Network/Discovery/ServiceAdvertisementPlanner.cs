using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using MacroDeckHost.Application.Configuration;

namespace MacroDeckHost.Application.Network.Discovery;

public static class ServiceAdvertisementPlanner
{
	public const string ServiceType = "_macrodeck._tcp";

	private const int MaximumLabelBytes = 63;

	private static readonly string[] VirtualNamePrefixes =
	[
		"vEthernet", "docker", "br-", "veth", "virbr", "vmnet", "bridge", "utun", "tun", "tap", "wg", "zt",
		"tailscale", "ipsec", "awdl", "llw", "anpi"
	];

	private static readonly string[] VirtualDescriptionMarkers =
	[
		"Hyper-V", "WSL", "Docker", "VirtualBox", "VMware", "TAP-", "WireGuard", "Tailscale", "ZeroTier", "OpenVPN",
		"Virtual", "Loopback"
	];

	public static ServiceAdvertisement? Plan(bool enabled,
		PublicEndpointSet endpoints,
		string instanceName,
		string version,
		IReadOnlyList<NetworkInterfaceSnapshot> interfaces)
	{
		// Only plain HTTP is advertised: the companion connects to a discovered host over HTTP, so a TLS-only
		// listener or a missing public listener means there is nothing a browsing client could use.
		if (!enabled || endpoints.HttpPort is not { } port || string.IsNullOrWhiteSpace(instanceName))
		{
			return null;
		}

		var advertised = interfaces
			.Where(IsLanInterface)
			.Select(nic => (nic.Index, Address: nic.Ipv4Addresses.FirstOrDefault(IsLanAddress)))
			.Where(entry => entry.Address is not null)
			.Select(entry => new AdvertisedInterface(entry.Index, entry.Address!))
			.DistinctBy(entry => entry.Index)
			.OrderBy(entry => entry.Index)
			.ToList();

		if (advertised.Count == 0)
		{
			return null;
		}

		return new ServiceAdvertisement(ToDnsLabel(instanceName),
			port,
			[new TxtEntry("name", instanceName), new TxtEntry("version", version)],
			advertised);
	}

	private static bool IsLanInterface(NetworkInterfaceSnapshot nic)
		=> nic.IsUp &&
			nic.Index > 0 &&
			nic.Type is not (NetworkInterfaceType.Loopback
				or NetworkInterfaceType.Tunnel
				or NetworkInterfaceType.Ppp) &&
			!VirtualNamePrefixes.Any(prefix => nic.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) &&
			!VirtualDescriptionMarkers.Any(marker =>
				nic.Description.Contains(marker, StringComparison.OrdinalIgnoreCase));

	private static bool IsLanAddress(IPAddress address)
	{
		if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
		{
			return false;
		}

		var bytes = address.GetAddressBytes();
		return !(bytes[0] == 169 && bytes[1] == 254);
	}

	private static string ToDnsLabel(string name)
	{
		if (Encoding.UTF8.GetByteCount(name) <= MaximumLabelBytes)
		{
			return name;
		}

		var builder = new StringBuilder();
		var length = 0;
		foreach (var rune in name.EnumerateRunes())
		{
			if (length + rune.Utf8SequenceLength > MaximumLabelBytes)
			{
				break;
			}

			builder.Append(rune.ToString());
			length += rune.Utf8SequenceLength;
		}

		return builder.ToString();
	}
}
