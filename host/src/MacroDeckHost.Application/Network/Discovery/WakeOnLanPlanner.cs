namespace MacroDeckHost.Application.Network.Discovery;

public static class WakeOnLanPlanner
{
	private const int MacAddressBytes = 6;

	public static IReadOnlyList<string> MacAddresses(IReadOnlyList<NetworkInterfaceSnapshot> interfaces)
		=> interfaces
			.Where(ServiceAdvertisementPlanner.IsLanInterface)
			.Where(nic => nic.Ipv4Addresses.Any(ServiceAdvertisementPlanner.IsLanAddress))
			.OrderBy(nic => nic.Index)
			.Select(nic => ToMacAddress(nic.PhysicalAddress))
			.OfType<string>()
			.Distinct()
			.ToList();

	private static string? ToMacAddress(string physicalAddress)
	{
		if (physicalAddress.Length != MacAddressBytes * 2 || !physicalAddress.All(Uri.IsHexDigit))
		{
			return null;
		}

		if (physicalAddress.All(digit => digit == '0'))
		{
			return null;
		}

		return string.Join(':', physicalAddress.ToUpperInvariant().Chunk(2).Select(pair => new string(pair)));
	}
}
