using MacroDeckHost.Integrations.Meld;

namespace MacroDeckHost.Tests.UnitTests.Meld;

[TestFixture]
internal sealed class MeldEndpointTests
{
	[Test]
	public void Build_uses_a_hostname()
	{
		var uri = MeldEndpoint.Build("streaming-pc", 13376);

		Assert.That(uri.ToString(), Is.EqualTo("ws://streaming-pc:13376/"));
	}

	[Test]
	public void Build_uses_an_ipv4_literal()
	{
		var uri = MeldEndpoint.Build("127.0.0.1", 13376);

		Assert.That(uri.ToString(), Is.EqualTo("ws://127.0.0.1:13376/"));
	}

	[Test]
	public void Build_brackets_an_ipv6_literal()
	{
		var uri = MeldEndpoint.Build("::1", 13376);

		Assert.That(uri.ToString(), Is.EqualTo("ws://[::1]:13376/"));
	}

	[TestCase("127.0.0.1")]
	[TestCase("localhost")]
	[TestCase("LOCALHOST")]
	[TestCase("::1")]
	public void IsLoopback_is_true_for_loopback_hosts(string host)
	{
		Assert.That(MeldEndpoint.IsLoopback(host), Is.True);
	}

	[TestCase("192.168.1.5")]
	[TestCase("streaming-pc.local")]
	public void IsLoopback_is_false_for_non_loopback_hosts(string host)
	{
		Assert.That(MeldEndpoint.IsLoopback(host), Is.False);
	}
}
