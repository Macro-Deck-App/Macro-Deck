using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Network.Tls;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Tests.UnitTests.Configuration;

[NonParallelizable]
public class PublicListenerFallbackTests
{
	[Test]
	public async Task Host_starts_on_loopback_only_when_the_public_port_is_held()
	{
		using var held = HoldPortOnAny();
		var heldPort = ((IPEndPoint)held.LocalEndPoint!).Port;

		var plan = HostListenerPlan.Create(PublicEndpointSet.HttpOnly(heldPort), 0);

		using var host = await StartMinimalHostAsync(plan.Apply);
		try
		{
			var loopbackPort = GetBoundLoopbackPort(host);
			using var client = new HttpClient();
			var response = await client.GetAsync($"http://127.0.0.1:{loopbackPort}/ping");

			Assert.Multiple(() =>
			{
				Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
				Assert.That(plan.PublicListenerAvailable, Is.False);
				Assert.That(plan.Endpoints.PublicPort, Is.EqualTo(heldPort));
			});
		}
		finally
		{
			await host.StopAsync();
		}
	}

	[Test]
	public void A_bare_ListenAnyIP_on_a_held_port_throws_on_start()
	{
		using var held = HoldPortOnAny();
		var heldPort = ((IPEndPoint)held.LocalEndPoint!).Port;

		Assert.ThrowsAsync<IOException>(async () =>
		{
			using var host = await StartMinimalHostAsync(options =>
			{
				options.ListenAnyIP(heldPort);
				options.Listen(IPAddress.Loopback, 0);
			});
			await host.StopAsync();
		});
	}

	[Test]
	public async Task Host_starts_normally_and_the_public_listener_answers_when_the_port_is_free()
	{
		var publicPort = GetFreeTcpPort();
		var plan = HostListenerPlan.Create(PublicEndpointSet.HttpOnly(publicPort), 0);

		using var host = await StartMinimalHostAsync(plan.Apply);
		try
		{
			using var client = new HttpClient();
			var response = await client.GetAsync($"http://127.0.0.1:{publicPort}/ping");

			Assert.Multiple(() =>
			{
				Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
				Assert.That(plan.PublicListenerAvailable, Is.True);
			});
		}
		finally
		{
			await host.StopAsync();
		}
	}

	[Test]
	public async Task Server_addresses_contain_only_the_loopback_listener_when_the_public_port_is_held()
	{
		using var held = HoldPortOnAny();
		var heldPort = ((IPEndPoint)held.LocalEndPoint!).Port;
		var plan = HostListenerPlan.Create(PublicEndpointSet.HttpOnly(heldPort), 0);

		using var host = await StartMinimalHostAsync(plan.Apply);
		try
		{
			var addresses = GetServerAddresses(host);
			var loopbackPort = GetBoundLoopbackPort(host);

			Assert.Multiple(() =>
			{
				Assert.That(addresses, Has.Count.EqualTo(1));
				Assert.That(addresses.Single(),
					Does.Contain(loopbackPort.ToString(CultureInfo.InvariantCulture)));
			});
		}
		finally
		{
			await host.StopAsync();
		}
	}

	[Test]
	public void Probe_of_a_free_port_reports_bindable_and_leaves_it_immediately_bindable_afterwards()
	{
		var port = GetFreeTcpPort();

		var result = PublicListenerProbe.Probe(port);
		Assert.That(result.CanBind, Is.True);

		var listener = new TcpListener(IPAddress.Any, port);
		Assert.DoesNotThrow(() => listener.Start(), "the probe must have released the port again");
		listener.Stop();
	}

	[Test]
	public void Probe_of_a_held_port_reports_cannot_bind_without_throwing()
	{
		using var held = HoldPortOnAny();
		var heldPort = ((IPEndPoint)held.LocalEndPoint!).Port;

		PublicListenerProbeResult result = default;
		Assert.DoesNotThrow(() => result = PublicListenerProbe.Probe(heldPort));
		Assert.That(result.CanBind, Is.False);
	}

	// 6. All bind failures count. No test here can portably reproduce a Windows reserved-range bind
	// failure (SocketException 10013) - that mapping is what the injectable probe seam exists to cover.
	[TestCase(SocketError.AccessDenied)]
	[TestCase(SocketError.AddressAlreadyInUse)]
	[TestCase(SocketError.AddressNotAvailable)]
	public void All_bind_failures_map_to_public_listener_unavailable_without_throwing(SocketError error)
	{
		var port = GetFreeTcpPort();
		HostListenerPlan? plan = null;

		Assert.DoesNotThrow(() =>
			plan = HostListenerPlan.Create(PublicEndpointSet.HttpOnly(port),
				0,
				certificate: null,
				probe: _ => new PublicListenerProbeResult(false, error, 10013)));

		Assert.That(plan!.PublicListenerAvailable, Is.False);
	}

	// 7. HTTPS on its own port: both public listeners answer, each on its own protocol, and the
	// loopback listener stays plain HTTP - the desktop shell and the plugin SDK both depend on that.
	[Test]
	public async Task Both_public_listeners_answer_and_the_loopback_listener_stays_plaintext()
	{
		using var certificate = CreateTestCertificate();
		var httpPort = GetFreeTcpPort();
		var httpsPort = GetFreeTcpPort();
		var plan = HostListenerPlan.Create(PublicEndpointSet.HttpAndHttps(httpPort, httpsPort), 0, certificate);

		using var host = await StartMinimalHostAsync(plan.Apply);
		try
		{
			using var client = CreateCertificateIgnoringClient();
			var overHttp = await client.GetAsync($"http://127.0.0.1:{httpPort}/ping");
			var overHttps = await client.GetAsync($"https://127.0.0.1:{httpsPort}/ping");
			var overLoopback = await client.GetAsync($"http://127.0.0.1:{GetBoundLoopbackPort(host)}/ping");

			Assert.Multiple(() =>
			{
				Assert.That(overHttp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
				Assert.That(overHttps.StatusCode, Is.EqualTo(HttpStatusCode.OK));
				Assert.That(overLoopback.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			});
		}
		finally
		{
			await host.StopAsync();
		}
	}

	[Test]
	public async Task Replace_mode_serves_tls_on_the_public_port_and_nothing_in_plaintext()
	{
		using var certificate = CreateTestCertificate();
		var publicPort = GetFreeTcpPort();
		var plan = HostListenerPlan.Create(PublicEndpointSet.HttpsReplacingHttp(publicPort), 0, certificate);

		using var host = await StartMinimalHostAsync(plan.Apply);
		try
		{
			using var client = CreateCertificateIgnoringClient();
			var overHttps = await client.GetAsync($"https://127.0.0.1:{publicPort}/ping");

			Assert.That(overHttps.StatusCode, Is.EqualTo(HttpStatusCode.OK));

			// A plaintext request to a TLS listener must not reach the application at all.
			Assert.That(async () => await client.GetAsync($"http://127.0.0.1:{publicPort}/ping"),
				Throws.InstanceOf<HttpRequestException>());
		}
		finally
		{
			await host.StopAsync();
		}
	}

	[Test]
	public async Task A_certificate_that_cannot_be_loaded_leaves_no_public_listener_in_replace_mode()
	{
		var publicPort = GetFreeTcpPort();
		var plan = HostListenerPlan.Create(PublicEndpointSet.HttpsReplacingHttp(publicPort), 0, certificate: null);

		using var host = await StartMinimalHostAsync(plan.Apply);
		try
		{
			using var client = CreateCertificateIgnoringClient();
			var overLoopback = await client.GetAsync($"http://127.0.0.1:{GetBoundLoopbackPort(host)}/ping");

			Assert.Multiple(() =>
			{
				Assert.That(plan.PublicListenerAvailable, Is.False);
				Assert.That(GetServerAddresses(host), Has.Count.EqualTo(1));
				Assert.That(overLoopback.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the desktop UI still works");
			});

			Assert.That(async () => await client.GetAsync($"http://127.0.0.1:{publicPort}/ping"),
				Throws.InstanceOf<HttpRequestException>());
		}
		finally
		{
			await host.StopAsync();
		}
	}

	[Test]
	public async Task A_certificate_that_cannot_be_loaded_leaves_the_http_listener_serving()
	{
		var httpPort = GetFreeTcpPort();
		var httpsPort = GetFreeTcpPort();
		var plan = HostListenerPlan.Create(PublicEndpointSet.HttpAndHttps(httpPort, httpsPort),
			0,
			certificate: null);

		using var host = await StartMinimalHostAsync(plan.Apply);
		try
		{
			using var client = CreateCertificateIgnoringClient();
			var overHttp = await client.GetAsync($"http://127.0.0.1:{httpPort}/ping");

			Assert.Multiple(() =>
			{
				Assert.That(plan.PublicListenerAvailable, Is.True);
				Assert.That(plan.Endpoints.HttpsPort, Is.Null);
				Assert.That(overHttp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			});
		}
		finally
		{
			await host.StopAsync();
		}
	}

	// 11. A held HTTPS port must not take the working HTTP listener down with it.
	[Test]
	public async Task A_held_https_port_leaves_the_http_and_loopback_listeners_serving()
	{
		using var certificate = CreateTestCertificate();
		using var held = HoldPortOnAny();
		var heldPort = ((IPEndPoint)held.LocalEndPoint!).Port;
		var httpPort = GetFreeTcpPort();
		var plan = HostListenerPlan.Create(PublicEndpointSet.HttpAndHttps(httpPort, heldPort), 0, certificate);

		using var host = await StartMinimalHostAsync(plan.Apply);
		try
		{
			using var client = CreateCertificateIgnoringClient();
			var overHttp = await client.GetAsync($"http://127.0.0.1:{httpPort}/ping");

			Assert.Multiple(() =>
			{
				Assert.That(plan.Endpoints.HttpsPort, Is.Null);
				Assert.That(plan.Endpoints.HttpPort, Is.EqualTo(httpPort));
				Assert.That(overHttp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			});
		}
		finally
		{
			await host.StopAsync();
		}
	}

	private static X509Certificate2 CreateTestCertificate()
	{
		using var rsa = RSA.Create(2048);
		var request = new CertificateRequest("CN=Macro Deck Listener Tests",
			rsa,
			HashAlgorithmName.SHA256,
			RSASignaturePadding.Pkcs1);
		request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));

		var san = new SubjectAlternativeNameBuilder();
		san.AddIpAddress(IPAddress.Loopback);
		request.CertificateExtensions.Add(san.Build());

		using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
			DateTimeOffset.UtcNow.AddDays(1));

		return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pkcs12),
			password: null,
			PublicTlsServerCertificate.KeyStorageFlags);
	}

	private static HttpClient CreateCertificateIgnoringClient()
		=> new(new HttpClientHandler
		{
			ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
		});

	private static async Task<IHost> StartMinimalHostAsync(Action<KestrelServerOptions> configureKestrel)
	{
		var host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
			.ConfigureWebHost(webBuilder =>
			{
				webBuilder.UseKestrel(configureKestrel);
				webBuilder.Configure(app =>
				{
					app.Run(async context =>
					{
						if (context.Request.Path == "/ping")
						{
							context.Response.StatusCode = 200;
							await context.Response.WriteAsync("pong");
							return;
						}

						context.Response.StatusCode = 404;
					});
				});
			})
			.Build();

		await host.StartAsync();
		return host;
	}

	private static ICollection<string> GetServerAddresses(IHost host)
		=> host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;

	private static int GetBoundLoopbackPort(IHost host)
		=> GetServerAddresses(host)
			.Select(address => new Uri(address))
			.Where(uri => uri.Host is "127.0.0.1" or "localhost")
			.Select(uri => uri.Port)
			.Single();

	private static Socket HoldPortOnAny()
	{
		var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp)
		{
			DualMode = true
		};
		socket.Bind(new IPEndPoint(IPAddress.IPv6Any, 0));
		socket.Listen();
		return socket;
	}

	private static int GetFreeTcpPort()
	{
		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		try
		{
			return ((IPEndPoint)listener.LocalEndpoint).Port;
		}
		finally
		{
			listener.Stop();
		}
	}
}
