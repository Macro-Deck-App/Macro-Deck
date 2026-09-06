using MacroDeckHost.Application.Configuration;

namespace MacroDeckHost.Tests.UnitTests.Configuration;

[NonParallelizable]
public class ResolvedPublicEndpointsTests
{
	[SetUp]
	[TearDown]
	public void ResetResolvedEndpoints() => ResolvedPublicEndpoints.ResetForTests();

	[Test]
	public void Falls_back_to_a_plain_http_listener_on_the_build_port_until_it_is_resolved()
	{
		Assert.Multiple(() =>
		{
			Assert.That(ResolvedPublicEndpoints.Value.HttpPort, Is.EqualTo(BuildConfig.PublicPort));
			Assert.That(ResolvedPublicEndpoints.Value.HttpsPort, Is.Null);
			Assert.That(ResolvedPublicEndpoints.Value.TlsMode, Is.EqualTo(PublicTlsMode.Disabled));
		});
	}

	[Test]
	public void The_resolved_public_port_is_what_the_host_endpoints_report()
	{
		ResolvedPublicEndpoints.Set(PublicEndpointSet.HttpOnly(9321));

		Assert.Multiple(() =>
		{
			Assert.That(ResolvedPublicEndpoints.Value.PublicPort, Is.EqualTo(9321));
			Assert.That(HostEndpoints.PublicPort, Is.EqualTo(9321));
		});
	}

	[Test]
	public void Publishing_the_same_listeners_twice_is_allowed()
	{
		var endpoints = PublicEndpointSet.HttpOnly(9321);
		ResolvedPublicEndpoints.Set(endpoints);

		Assert.DoesNotThrow(() => ResolvedPublicEndpoints.Set(endpoints));
	}

	// Value equality, not reference equality: the entry point resolves the set once, but a caller
	// reconstructing the identical configuration must not be treated as a conflicting change.
	[Test]
	public void Publishing_an_equal_set_built_separately_is_allowed()
	{
		ResolvedPublicEndpoints.Set(PublicEndpointSet.HttpAndHttps(9321, 9322));

		Assert.DoesNotThrow(() => ResolvedPublicEndpoints.Set(PublicEndpointSet.HttpAndHttps(9321, 9322)));
	}

	[Test]
	public void Changing_the_resolved_listeners_throws()
	{
		ResolvedPublicEndpoints.Set(PublicEndpointSet.HttpOnly(9321));

		Assert.Throws<InvalidOperationException>(() => ResolvedPublicEndpoints.Set(PublicEndpointSet.HttpOnly(9322)));
	}

	[Test]
	public void Changing_only_the_protocol_on_the_same_port_throws()
	{
		ResolvedPublicEndpoints.Set(PublicEndpointSet.HttpOnly(9321));

		Assert.Throws<InvalidOperationException>(() =>
			ResolvedPublicEndpoints.Set(PublicEndpointSet.HttpsReplacingHttp(9321)));
	}

	[Test]
	public void Resolving_to_the_build_default_still_blocks_a_later_change()
	{
		ResolvedPublicEndpoints.Set(PublicEndpointSet.HttpOnly(BuildConfig.DefaultPublicPort));

		Assert.Throws<InvalidOperationException>(() => ResolvedPublicEndpoints.Set(PublicEndpointSet.HttpOnly(9322)));
	}
}
