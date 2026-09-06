using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Network.Tls;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Api;

[NonParallelizable]
public class DeviceSetupEndpointTests
{
	private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

	private string _dataDir = null!;
	private string? _previousDataDir;

	[SetUp]
	public void SetUp()
	{
		_dataDir = Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"));
		_previousDataDir = Environment.GetEnvironmentVariable("MACRODECK_DATA_DIR");
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _dataDir);

		DatabaseMigrationHelper.MigrateDatabase(new MacroDeckPaths());
	}

	[TearDown]
	public void TearDown()
	{
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _previousDataDir);
		if (Directory.Exists(_dataDir))
		{
			Directory.Delete(_dataDir, recursive: true);
		}
	}

	// A device that does not trust the certificate yet cannot sign in, so the material it needs to fix
	// that has to be reachable without signing in.
	[Test]
	public async Task An_Unpaired_Device_Can_Download_The_Certificate_Authority()
	{
		using var host = await StartHost(new FakeCertificateStore(Now));
		using var client = host.GetTestClient();

		var response = await client.GetAsync("/api/device-setup/certificate-authority.crt");
		var body = await response.Content.ReadAsByteArrayAsync();
		using var downloaded = X509CertificateLoader.LoadCertificate(body);
		var basicConstraints = downloaded.Extensions.OfType<X509BasicConstraintsExtension>().Single();

		Assert.Multiple(() =>
		{
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/x-x509-ca-cert"));
			Assert.That(basicConstraints.CertificateAuthority, Is.True);
		});

		await host.StopAsync();
	}

	[Test]
	public async Task The_Download_Carries_No_Private_Key_Material()
	{
		using var host = await StartHost(new FakeCertificateStore(Now));
		using var client = host.GetTestClient();

		var body = await (await client.GetAsync("/api/device-setup/certificate-authority.crt"))
			.Content.ReadAsByteArrayAsync();
		using var downloaded = X509CertificateLoader.LoadCertificate(body);

		Assert.Multiple(() =>
		{
			Assert.That(downloaded.HasPrivateKey, Is.False);
			Assert.That(Encoding.ASCII.GetString(body), Does.Not.Contain("PRIVATE KEY"));
		});

		await host.StopAsync();
	}

	[Test]
	public async Task The_Setup_State_Names_The_Addresses_The_Host_Certificate_Covers()
	{
		using var host = await StartHost(new FakeCertificateStore(Now));
		using var client = host.GetTestClient();

		var response = await client.GetAsync("/api/device-setup");
		var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
		var names = body.GetProperty("hostCertificateSubjectAlternativeNames")
			.EnumerateArray()
			.Select(name => name.GetString())
			.ToList();

		Assert.Multiple(() =>
		{
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(body.GetProperty("certificateAuthority").GetProperty("available").GetBoolean(), Is.True);
			Assert.That(body.GetProperty("certificateAuthority").GetProperty("fingerprintSha256").GetString(),
				Is.Not.Empty);
			Assert.That(names, Does.Contain("localhost"));
			Assert.That(names, Does.Contain("deck-pc.local"));
		});

		await host.StopAsync();
	}

	[Test]
	public async Task Nothing_Is_Offered_When_No_Authority_Exists()
	{
		using var host = await StartHost(new FakeCertificateStore(Now) { HasAuthority = false });
		using var client = host.GetTestClient();

		var download = await client.GetAsync("/api/device-setup/certificate-authority.crt");
		var state = JsonDocument.Parse(await (await client.GetAsync("/api/device-setup")).Content.ReadAsStringAsync())
			.RootElement;

		Assert.Multiple(() =>
		{
			Assert.That(download.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
			Assert.That(state.GetProperty("certificateAuthority").GetProperty("available").GetBoolean(), Is.False);
		});

		await host.StopAsync();
	}

	// The anonymous route must not have widened its neighbours.
	[Test]
	public async Task The_Network_Settings_Endpoint_Still_Requires_An_Administrator()
	{
		using var host = await StartHost(new FakeCertificateStore(Now));
		using var client = host.GetTestClient();

		var response = await client.GetAsync("/api/settings/network");

		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

		await host.StopAsync();
	}

	private static async Task<IHost> StartHost(IPublicTlsCertificateStore certificateStore)
	{
		var listenerState = new FakeHostListenerState
		{
			PublicEndpoints = PublicEndpointSet.HttpAndHttps(9100, 9101)
		};

		// Deliberately without the loopback startup filter the other endpoint tests install: the request
		// has to look like it came from an unpaired device on the network, not from the desktop app.
		return await new HostBuilder()
			.ConfigureWebHost(builder =>
			{
				builder.UseTestServer();
				builder.UseStartup<Startup>();
				builder.ConfigureTestServices(services =>
				{
					services.RemoveAll<IHostedService>();
					services.AddSingleton(Log.Logger);
					services.RemoveAll<StartupReadiness>();
					services.AddSingleton(CompletedStartupReadiness());
					services.RemoveAll<IHostListenerState>();
					services.AddSingleton<IHostListenerState>(listenerState);
					services.RemoveAll<IPublicTlsCertificateStore>();
					services.AddSingleton(certificateStore);
				});
			})
			.StartAsync();
	}

	private static StartupReadiness CompletedStartupReadiness()
	{
		var readiness = new StartupReadiness();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();
		return readiness;
	}

	private sealed class FakeCertificateStore : IPublicTlsCertificateStore
	{
		private readonly GeneratedCertificate _authority;
		private readonly GeneratedCertificate _host;

		public FakeCertificateStore(DateTimeOffset now)
		{
			_authority = LocalCertificateAuthority.CreateAuthority("deck-pc", now);
			_host = LocalCertificateAuthority.IssueHostCertificate(_authority,
				[IPAddress.Parse("192.168.1.42")],
				["deck-pc", "deck-pc.local"],
				now);
		}

		public bool HasAuthority { get; init; } = true;

		public PublicTlsCertificateInfo? ReadInfo() => null;

		public string? ReadCertificatePem() => _host.CertificatePem;

		public PublicTlsCertificateResolution LoadServerCertificate()
			=> new(null, PublicTlsFailure.NotConfigured);

		public PublicTlsCertificateInfo Save(string certificatePem,
			string privateKeyPem,
			PublicTlsCertificateSource source)
			=> throw new NotSupportedException();

		public PublicTlsCertificateInfo? ReadAuthorityInfo()
		{
			if (!HasAuthority)
			{
				return null;
			}

			using var certificate = X509Certificate2.CreateFromPem(_authority.CertificatePem);
			return new PublicTlsCertificateInfo(certificate.Subject,
				certificate.GetCertHashString(HashAlgorithmName.SHA256),
				certificate.NotBefore,
				certificate.NotAfter,
				PublicTlsCertificateSource.LocalCa);
		}

		public string? ReadAuthorityCertificatePem() => HasAuthority ? _authority.CertificatePem : null;

		public PublicTlsAuthorityResolution LoadAuthority()
			=> new(HasAuthority ? _authority : null, PublicTlsFailure.None);

		public PublicTlsCertificateInfo SaveAuthority(string certificatePem, string privateKeyPem)
			=> throw new NotSupportedException();
	}
}
