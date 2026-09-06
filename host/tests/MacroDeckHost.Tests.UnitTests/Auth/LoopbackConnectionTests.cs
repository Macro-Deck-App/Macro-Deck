using System.Net;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Auth;
using Microsoft.AspNetCore.Http;

namespace MacroDeckHost.Tests.UnitTests.Auth;

[NonParallelizable]
public class LoopbackConnectionTests
{
	private const int LoopbackPort = TestListenerPorts.Loopback;
	private const int PublicHttpPort = 9100;
	private const int PublicHttpsPort = 9101;

	[SetUp]
	public void ResolveListeners()
	{
		ResolvedPublicEndpoints.ResetForTests();
		ResolvedLoopbackPort.ResetForTests();
		ResolvedLoopbackPort.Set(LoopbackPort);
	}

	[TearDown]
	public void ResetListeners()
	{
		ResolvedPublicEndpoints.ResetForTests();
		ResolvedLoopbackPort.ResetForTests();
		ResolvedLoopbackPort.Set(TestListenerPorts.Loopback);
	}

	private static DefaultHttpContext CreateContext(int localPort, IPAddress? remoteAddress, string host)
	{
		var context = new DefaultHttpContext
		{
			Connection = { LocalPort = localPort, RemoteIpAddress = remoteAddress }
		};
		context.Request.Host = new HostString(host, localPort);
		return context;
	}

	[Test]
	public void Loopback_listener_with_loopback_remote_and_host_is_trusted()
	{
		ResolvedPublicEndpoints.Set(PublicEndpointSet.HttpOnly(PublicHttpPort));

		Assert.Multiple(() =>
		{
			Assert.That(LoopbackConnection.IsTrusted(CreateContext(LoopbackPort, IPAddress.Loopback, "127.0.0.1")),
				Is.True);
			Assert.That(LoopbackConnection.IsTrusted(CreateContext(LoopbackPort, IPAddress.IPv6Loopback, "localhost")),
				Is.True);
			Assert.That(LoopbackConnection.IsTrusted(CreateContext(LoopbackPort, IPAddress.IPv6Loopback, "::1")),
				Is.True);
		});
	}

	// The trust boundary has to follow a port the user configured, not the value baked into the build.
	[Test]
	public void The_public_http_port_is_never_trusted_even_from_loopback()
	{
		ResolvedPublicEndpoints.Set(PublicEndpointSet.HttpOnly(PublicHttpPort));

		Assert.That(LoopbackConnection.IsTrusted(CreateContext(PublicHttpPort, IPAddress.Loopback, "127.0.0.1")),
			Is.False);
	}

	[Test]
	public void The_additional_https_port_is_never_trusted_even_from_loopback()
	{
		ResolvedPublicEndpoints.Set(PublicEndpointSet.HttpAndHttps(PublicHttpPort, PublicHttpsPort));

		Assert.Multiple(() =>
		{
			Assert.That(LoopbackConnection.IsTrusted(CreateContext(PublicHttpsPort, IPAddress.Loopback, "127.0.0.1")),
				Is.False);
			Assert.That(LoopbackConnection.IsTrusted(CreateContext(PublicHttpPort, IPAddress.Loopback, "127.0.0.1")),
				Is.False);
			Assert.That(LoopbackConnection.IsTrusted(CreateContext(LoopbackPort, IPAddress.Loopback, "127.0.0.1")),
				Is.True);
		});
	}

	// Replace mode puts HTTPS on the configured public port; trust must not follow the protocol.
	[Test]
	public void The_public_port_is_not_trusted_when_https_replaced_http_on_it()
	{
		ResolvedPublicEndpoints.Set(PublicEndpointSet.HttpsReplacingHttp(PublicHttpPort));

		Assert.That(LoopbackConnection.IsTrusted(CreateContext(PublicHttpPort, IPAddress.Loopback, "127.0.0.1")),
			Is.False);
	}

	[Test]
	public void Nothing_is_trusted_while_the_loopback_port_is_unknown()
	{
		ResolvedLoopbackPort.ResetForTests();
		ResolvedPublicEndpoints.Set(PublicEndpointSet.HttpOnly(PublicHttpPort));

		Assert.That(LoopbackConnection.IsTrusted(CreateContext(LoopbackPort, IPAddress.Loopback, "127.0.0.1")),
			Is.False);
	}

	[Test]
	public void Remote_address_must_be_loopback()
	{
		Assert.Multiple(() =>
		{
			Assert.That(LoopbackConnection.IsTrusted(CreateContext(LoopbackPort,
					IPAddress.Parse("192.168.1.10"),
					"127.0.0.1")),
				Is.False);
			Assert.That(LoopbackConnection.IsTrusted(CreateContext(LoopbackPort, null, "127.0.0.1")), Is.False);
		});
	}

	[Test]
	public void Non_loopback_host_header_is_rejected_dns_rebinding_guard()
	{
		Assert.Multiple(() =>
		{
			Assert.That(
				LoopbackConnection.IsTrusted(CreateContext(LoopbackPort, IPAddress.Loopback, "evil.example.com")),
				Is.False);
			Assert.That(LoopbackConnection.IsTrusted(CreateContext(LoopbackPort, IPAddress.Loopback, "192.168.1.10")),
				Is.False);
		});
	}

	[Test]
	public void Local_requests_are_recognised_on_every_listener_without_being_trusted()
	{
		ResolvedPublicEndpoints.Set(PublicEndpointSet.HttpAndHttps(PublicHttpPort, PublicHttpsPort));

		foreach (var port in (int[])[LoopbackPort, PublicHttpPort, PublicHttpsPort])
		{
			var context = CreateContext(port, IPAddress.Loopback, "127.0.0.1");
			Assert.That(LoopbackConnection.IsLocalRequest(context), Is.True, $"port {port}");
		}

		Assert.Multiple(() =>
		{
			Assert.That(LoopbackConnection.IsTrusted(CreateContext(PublicHttpPort, IPAddress.Loopback, "127.0.0.1")),
				Is.False);
			Assert.That(LoopbackConnection.IsTrusted(CreateContext(PublicHttpsPort, IPAddress.Loopback, "127.0.0.1")),
				Is.False);
		});
	}
}
