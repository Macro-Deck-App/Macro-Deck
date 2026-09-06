using MacroDeckHost.Application.Configuration;

namespace MacroDeckHost.Tests.UnitTests.Configuration;

public class PublicEndpointSetTests
{
	private const int PublicPort = 8193;
	private const int HttpsPort = 8194;
	private const int LoopbackPort = 54321;

	[Test]
	public void Without_tls_the_only_public_listener_is_http_on_the_public_port()
	{
		var endpoints = PublicEndpointSet.HttpOnly(PublicPort);

		Assert.Multiple(() =>
		{
			Assert.That(endpoints.Endpoints, Is.EqualTo(new[] { new PublicEndpoint(PublicPort, false) }));
			Assert.That(endpoints.HttpsPort, Is.Null);
			Assert.That(endpoints.IsPublicPort(PublicPort), Is.True);
			Assert.That(endpoints.IsPublicPort(HttpsPort), Is.False);
		});
	}

	// Replace mode moves HTTPS onto the port the user configured; it must not quietly serve on the
	// separate HTTPS port instead, which is a port nobody was told about.
	[Test]
	public void Replace_mode_serves_https_on_the_public_port_and_offers_no_http_at_all()
	{
		var endpoints = PublicEndpointSet.HttpsReplacingHttp(PublicPort);

		Assert.Multiple(() =>
		{
			Assert.That(endpoints.Endpoints, Is.EqualTo(new[] { new PublicEndpoint(PublicPort, true) }));
			Assert.That(endpoints.HttpPort, Is.Null);
			Assert.That(endpoints.HttpsPort, Is.EqualTo(PublicPort));
			Assert.That(endpoints.IsPublicPort(HttpsPort), Is.False);
		});
	}

	// Plain HTTP first, expressed once here so the connection panel and the QR payload cannot disagree.
	// HTTPS is on by default now, so a client that takes the first entry rather than trying them in
	// order would otherwise be sent, on the upgrade that enables it, to an origin whose certificate
	// authority it has not installed yet. The setup wizard moves a device to HTTPS once it can verify it.
	[Test]
	public void Additional_mode_serves_both_with_http_listed_first()
	{
		var endpoints = PublicEndpointSet.HttpAndHttps(PublicPort, HttpsPort);

		Assert.That(endpoints.Endpoints,
			Is.EqualTo(new[] { new PublicEndpoint(PublicPort, false), new PublicEndpoint(HttpsPort, true) }));
	}

	[Test]
	public void Both_public_ports_are_recognised_as_public_and_the_loopback_port_is_not()
	{
		var endpoints = PublicEndpointSet.HttpAndHttps(PublicPort, HttpsPort);

		Assert.Multiple(() =>
		{
			Assert.That(endpoints.IsPublicPort(PublicPort), Is.True);
			Assert.That(endpoints.IsPublicPort(HttpsPort), Is.True);
			Assert.That(endpoints.IsPublicPort(LoopbackPort), Is.False);
		});
	}

	// The OAuth callback is opened in a browser and the adb tunnel is consumed by the companion app;
	// both prefer plaintext because a self-signed certificate breaks a redirect and several OAuth
	// providers refuse an https loopback redirect URI outright.
	[Test]
	public void Local_clients_are_pointed_at_http_whenever_a_public_http_listener_exists()
	{
		Assert.Multiple(() =>
		{
			Assert.That(PublicEndpointSet.HttpOnly(PublicPort).LocalClientEndpoint,
				Is.EqualTo(new PublicEndpoint(PublicPort, false)));
			Assert.That(PublicEndpointSet.HttpAndHttps(PublicPort, HttpsPort).LocalClientEndpoint,
				Is.EqualTo(new PublicEndpoint(PublicPort, false)));
		});
	}

	[Test]
	public void Local_clients_fall_back_to_https_only_when_there_is_no_http_listener()
	{
		Assert.That(PublicEndpointSet.HttpsReplacingHttp(PublicPort).LocalClientEndpoint,
			Is.EqualTo(new PublicEndpoint(PublicPort, true)));
	}

	[Test]
	public void Dropping_https_in_replace_mode_leaves_no_public_listener_rather_than_plain_http()
	{
		var endpoints = PublicEndpointSet.HttpsReplacingHttp(PublicPort).WithoutHttps();

		Assert.Multiple(() =>
		{
			Assert.That(endpoints.Endpoints, Is.Empty);
			Assert.That(endpoints.HasPublicListener, Is.False);
			Assert.That(endpoints.LocalClientEndpoint, Is.Null);
			Assert.That(endpoints.IsPublicPort(PublicPort), Is.False);
			Assert.That(endpoints.PublicPort, Is.EqualTo(PublicPort));
			Assert.That(endpoints.TlsMode, Is.EqualTo(PublicTlsMode.Replace));
		});
	}

	[Test]
	public void Dropping_https_in_additional_mode_leaves_the_http_listener_serving()
	{
		var endpoints = PublicEndpointSet.HttpAndHttps(PublicPort, HttpsPort).WithoutHttps();

		Assert.Multiple(() =>
		{
			Assert.That(endpoints.Endpoints, Is.EqualTo(new[] { new PublicEndpoint(PublicPort, false) }));
			Assert.That(endpoints.HasPublicListener, Is.True);
		});
	}

	[Test]
	public void Dropping_http_in_additional_mode_leaves_https_serving_and_local_clients_pointed_at_it()
	{
		var endpoints = PublicEndpointSet.HttpAndHttps(PublicPort, HttpsPort).WithoutHttp();

		Assert.Multiple(() =>
		{
			Assert.That(endpoints.Endpoints, Is.EqualTo(new[] { new PublicEndpoint(HttpsPort, true) }));
			Assert.That(endpoints.HasPublicListener, Is.True);
			Assert.That(endpoints.LocalClientEndpoint, Is.EqualTo(new PublicEndpoint(HttpsPort, true)));
		});
	}
}
