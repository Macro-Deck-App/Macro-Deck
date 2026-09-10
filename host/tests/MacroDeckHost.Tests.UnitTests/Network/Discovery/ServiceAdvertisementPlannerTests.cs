using System.Net;
using System.Net.NetworkInformation;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Network.Discovery;

namespace MacroDeckHost.Tests.UnitTests.Network.Discovery;

[TestFixture]
internal sealed class ServiceAdvertisementPlannerTests
{
	private static readonly NetworkInterfaceSnapshot Wifi =
		Nic(17, "en0", "Wi-Fi", NetworkInterfaceType.Wireless80211, "192.168.20.13");

	private static NetworkInterfaceSnapshot Nic(int index,
		string name,
		string description,
		NetworkInterfaceType type,
		params string[] addresses)
		=> new(index, name, description, type, true, addresses.Select(IPAddress.Parse).ToList());

	private static ServiceAdvertisement? Plan(PublicEndpointSet? endpoints = null,
		bool enabled = true,
		string name = "Studio PC",
		params NetworkInterfaceSnapshot[] interfaces)
		=> ServiceAdvertisementPlanner.Plan(enabled,
			endpoints ?? PublicEndpointSet.HttpOnly(8193),
			name,
			"3.1.0",
			interfaces.Length == 0 ? [Wifi] : interfaces);

	[Test]
	public void The_txt_record_carries_the_instance_name_and_the_version_under_lower_case_keys()
	{
		var plan = Plan();

		Assert.That(plan!.Txt,
			Is.EqualTo(new[] { new TxtEntry("name", "Studio PC"), new TxtEntry("version", "3.1.0") }));
	}

	[Test]
	public void The_service_port_is_the_plain_http_listener_even_when_https_runs_beside_it()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Plan(PublicEndpointSet.HttpOnly(8193))!.Port, Is.EqualTo(8193));
			Assert.That(Plan(PublicEndpointSet.HttpAndHttps(8194, 8195))!.Port, Is.EqualTo(8194));
		});
	}

	[Test]
	public void Nothing_is_advertised_without_a_plain_http_public_listener()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Plan(PublicEndpointSet.HttpsReplacingHttp(8193)), Is.Null);
			Assert.That(Plan(PublicEndpointSet.HttpOnly(8193).WithoutHttp()), Is.Null);
		});
	}

	[Test]
	public void Nothing_is_advertised_when_discovery_is_turned_off()
	{
		Assert.That(Plan(enabled: false), Is.Null);
	}

	[Test]
	public void Nothing_is_advertised_when_no_lan_interface_exists()
	{
		var loopback = Nic(1, "lo0", "Loopback", NetworkInterfaceType.Loopback, "127.0.0.1");

		Assert.That(Plan(interfaces: loopback), Is.Null);
	}

	[Test]
	public void Only_real_lan_interfaces_are_advertised()
	{
		var ethernet = Nic(5,
			"Ethernet",
			"Intel(R) Ethernet Connection I219-V",
			NetworkInterfaceType.Ethernet,
			"192.168.1.5");
		var interfaces = new[]
		{
			Wifi,
			ethernet,
			Nic(1, "lo0", "Loopback", NetworkInterfaceType.Loopback, "127.0.0.1"),
			Nic(6, "en5", "USB Ethernet", NetworkInterfaceType.Ethernet, "169.254.3.4"),
			Nic(22, "utun4", "utun4", NetworkInterfaceType.Unknown, "100.123.95.52"),
			Nic(30, "vEthernet (WSL)", "Hyper-V Virtual Ethernet Adapter", NetworkInterfaceType.Ethernet, "172.20.0.1"),
			Nic(31, "Ethernet 3", "Hyper-V Virtual Ethernet Adapter #2", NetworkInterfaceType.Ethernet, "172.21.0.1"),
			Nic(32, "docker0", "docker0", NetworkInterfaceType.Ethernet, "172.17.0.1"),
			Nic(33, "Teredo", "Teredo Tunneling Pseudo-Interface", NetworkInterfaceType.Tunnel, "10.9.9.9"),
			new NetworkInterfaceSnapshot(34,
				"en1",
				"Thunderbolt Ethernet",
				NetworkInterfaceType.Ethernet,
				false,
				[IPAddress.Parse("192.168.30.2")])
		};

		var plan = Plan(interfaces: interfaces);

		Assert.That(plan!.Interfaces,
			Is.EqualTo(new[]
			{
				new AdvertisedInterface(5, IPAddress.Parse("192.168.1.5")),
				new AdvertisedInterface(17, IPAddress.Parse("192.168.20.13"))
			}));
	}

	[Test]
	public void An_interface_with_a_link_local_and_a_lan_address_is_advertised_with_the_lan_address()
	{
		var nic = Nic(4, "eth0", "eth0", NetworkInterfaceType.Ethernet, "169.254.1.1", "10.0.0.2");

		Assert.That(Plan(interfaces: nic)!.Interfaces.Single().Ipv4, Is.EqualTo(IPAddress.Parse("10.0.0.2")));
	}

	[Test]
	public void A_name_longer_than_a_dns_label_is_shortened_on_the_wire_but_kept_whole_in_the_txt_record()
	{
		var name = new string('a', 62) + "ébc";

		var plan = Plan(name: name);

		Assert.Multiple(() =>
		{
			Assert.That(plan!.WireName, Is.EqualTo(new string('a', 62)));
			Assert.That(plan.Txt.Single(entry => entry.Key == "name").Value, Is.EqualTo(name));
		});
	}
}
