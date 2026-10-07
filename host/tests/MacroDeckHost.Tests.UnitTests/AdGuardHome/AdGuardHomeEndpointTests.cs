using MacroDeckHost.Integrations.AdGuardHome;

namespace MacroDeckHost.Tests.UnitTests.AdGuardHome;

[TestFixture]
internal sealed class AdGuardHomeEndpointTests
{
	[TestCase("http://192.168.1.2:3000", "http://192.168.1.2:3000/control/")]
	[TestCase("192.168.1.2:3000", "http://192.168.1.2:3000/control/")]
	[TestCase("https://dns.example.com/", "https://dns.example.com/control/")]
	[TestCase("https://dns.example.com/control", "https://dns.example.com/control/")]
	[TestCase("https://example.com/adguard/control/", "https://example.com/adguard/control/")]
	[TestCase("http://admin:pw@adguard.lan?x=1#y", "http://adguard.lan/control/")]
	public void Addresses_point_at_the_control_api(string input, string expected)
		=> Assert.That(AdGuardHomeEndpoint.TryBuild(input)?.ToString(), Is.EqualTo(expected));

	[TestCase("")]
	[TestCase("   ")]
	[TestCase("ftp://adguard.lan")]
	[TestCase("http://")]
	public void Invalid_addresses_are_rejected(string input)
		=> Assert.That(AdGuardHomeEndpoint.TryBuild(input), Is.Null);
}
