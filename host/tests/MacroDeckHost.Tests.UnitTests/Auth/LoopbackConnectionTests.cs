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

	private static DefaultHttpContext CreateContext(int localPort,
		IPAddress? remoteAddress,
		string host,
		bool withSecret = true)
	{
		var context = new DefaultHttpContext
		{
			Connection = { LocalPort = localPort, RemoteIpAddress = remoteAddress }
		};
		context.Request.Host = new HostString(host, localPort);
		if (withSecret)
		{
			context.Request.Headers[LoopbackSecret.HeaderName] = TestListenerPorts.LoopbackSecret;
		}

		return context;
	}

	private static DefaultHttpContext CreateLoopbackContext(string? secretHeader = null, string? cookie = null)
	{
		var context = CreateContext(LoopbackPort, IPAddress.Loopback, "127.0.0.1", withSecret: false);
		if (secretHeader is not null)
		{
			context.Request.Headers[LoopbackSecret.HeaderName] = secretHeader;
		}

		if (cookie is not null)
		{
			context.Request.Headers.Cookie = cookie;
		}

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

	[Test]
	public void A_loopback_caller_without_the_secret_is_not_trusted()
	{
		Assert.That(LoopbackConnection.IsTrusted(CreateLoopbackContext()), Is.False);
	}

	[Test]
	public void A_wrong_secret_is_not_trusted()
	{
		Assert.Multiple(() =>
		{
			Assert.That(LoopbackConnection.IsTrusted(CreateLoopbackContext(new string('0', 64))), Is.False);
			Assert.That(LoopbackConnection.IsTrusted(CreateLoopbackContext("not-hex")), Is.False);
			Assert.That(LoopbackConnection.IsTrusted(CreateLoopbackContext(TestListenerPorts.LoopbackSecret[..32])),
				Is.False);
		});
	}

	[Test]
	public void The_desktop_session_cookie_is_trusted_on_the_loopback_listener()
	{
		var cookie = $"md_loopback_{LoopbackPort}={LoopbackSecret.SessionCookieValue()}";

		Assert.That(LoopbackConnection.IsTrusted(CreateLoopbackContext(cookie: cookie)), Is.True);
	}

	[Test]
	public void A_session_cookie_is_not_trusted_under_another_port_name_or_on_the_public_listener()
	{
		ResolvedPublicEndpoints.Set(PublicEndpointSet.HttpOnly(PublicHttpPort));
		var value = LoopbackSecret.SessionCookieValue();
		var onPublic = CreateContext(PublicHttpPort, IPAddress.Loopback, "127.0.0.1", withSecret: false);
		onPublic.Request.Headers.Cookie = $"md_loopback_{PublicHttpPort}={value}";

		Assert.Multiple(() =>
		{
			Assert.That(LoopbackConnection.IsTrusted(CreateLoopbackContext(cookie: $"md_loopback_1234={value}")),
				Is.False);
			Assert.That(LoopbackConnection.IsTrusted(CreateLoopbackContext(cookie: $"md_loopback_{LoopbackPort}=00")),
				Is.False);
			Assert.That(LoopbackConnection.IsTrusted(onPublic), Is.False);
		});
	}

	[Test]
	public void A_cross_site_page_is_not_trusted_even_with_the_session_cookie()
	{
		var context = CreateLoopbackContext(cookie: $"md_loopback_{LoopbackPort}={LoopbackSecret.SessionCookieValue()}");
		context.Request.Headers["Sec-Fetch-Site"] = "cross-site";

		Assert.That(LoopbackConnection.IsTrusted(context), Is.False);
	}
}
