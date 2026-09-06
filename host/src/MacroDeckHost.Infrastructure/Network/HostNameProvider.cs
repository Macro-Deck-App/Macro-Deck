using System.Net;
using MacroDeckHost.Application.Network.Tls;

namespace MacroDeckHost.Infrastructure.Network;

public sealed class HostNameProvider : IHostNameProvider
{
	private const string MulticastDnsSuffix = ".local";

	public string MachineName => Environment.MachineName;

	public IReadOnlyList<string> GetHostNames()
	{
		var machineName = Environment.MachineName;
		if (!IsUsableDnsLabel(machineName))
		{
			return [];
		}

		// The multicast DNS name is the only address in the certificate that survives a DHCP change, so an
		// installed web app keeps its origin - and with it its storage and its session - across one.
		return machineName.EndsWith(MulticastDnsSuffix, StringComparison.OrdinalIgnoreCase)
			? [machineName]
			: [machineName, machineName + MulticastDnsSuffix];
	}

	private static bool IsUsableDnsLabel(string name)
	{
		if (string.IsNullOrWhiteSpace(name) || IPAddress.TryParse(name, out _))
		{
			return false;
		}

		return name.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '.');
	}
}
