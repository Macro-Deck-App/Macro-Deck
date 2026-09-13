using System.Net;
using System.Net.NetworkInformation;
using MacroDeckHost.Application.Network.Discovery;

namespace MacroDeckHost.Tests.UnitTests.Network.Discovery;

[TestFixture]
internal sealed class WakeOnLanPlannerTests
{
	private static NetworkInterfaceSnapshot Nic(int index,
		string name,
		string mac,
		NetworkInterfaceType type = NetworkInterfaceType.Ethernet,
		bool up = true,
		string description = "Ethernet",
		params string[] addresses)
		=> new(index,
			name,
			description,
			type,
			up,
			(addresses.Length == 0 ? ["192.168.20.13"] : addresses).Select(IPAddress.Parse).ToList(),
			mac);

	[Test]
	public void Every_lan_card_the_host_is_reachable_on_is_named_in_colon_form_in_interface_order()
	{
		var macs = WakeOnLanPlanner.MacAddresses([
			Nic(12, "en1", "a0b1c2d3e4f5", NetworkInterfaceType.Wireless80211, addresses: "192.168.20.14"),
			Nic(4, "en0", "001122334455")
		]);

		Assert.That(macs, Is.EqualTo(new[] { "00:11:22:33:44:55", "A0:B1:C2:D3:E4:F5" }));
	}

	[Test]
	public void Cards_a_companion_cannot_reach_the_host_on_are_left_out()
	{
		var macs = WakeOnLanPlanner.MacAddresses([
			Nic(1, "lo0", "000000000001", NetworkInterfaceType.Loopback),
			Nic(2, "en2", "001122334456", up: false),
			Nic(3, "vEthernet (WSL)", "001122334457"),
			Nic(5, "eth1", "001122334458", description: "VirtualBox Host-Only Ethernet Adapter"),
			Nic(6, "en3", "001122334459", addresses: "169.254.10.2"),
			Nic(7, "en4", "001122334460", addresses: "fe80::1")
		]);

		Assert.That(macs, Is.Empty);
	}

	[Test]
	public void A_card_without_a_usable_hardware_address_names_none()
	{
		var macs = WakeOnLanPlanner.MacAddresses([
			Nic(1, "en0", ""),
			Nic(2, "en1", "000000000000"),
			Nic(3, "en2", "00112233445566778899"),
			Nic(4, "en3", "0011223344ZZ")
		]);

		Assert.That(macs, Is.Empty);
	}

	[Test]
	public void A_card_listed_twice_is_named_once()
	{
		var macs = WakeOnLanPlanner.MacAddresses([Nic(1, "en0", "001122334455"), Nic(2, "en0.5", "001122334455")]);

		Assert.That(macs, Is.EqualTo(new[] { "00:11:22:33:44:55" }));
	}
}
