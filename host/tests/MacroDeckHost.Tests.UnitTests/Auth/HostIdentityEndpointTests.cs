using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Auth;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Ui;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Auth;

[NonParallelizable]
public class HostIdentityEndpointTests
{
	private const string LocalIp = "192.168.1.10";
	private static int _remoteCounter;

	private IHost _host = null!;
	private HttpClient _client = null!;
	private string _dataDir = null!;
	private string? _previousDataDir;

	[OneTimeSetUp]
	public async Task OneTimeSetUp()
	{
		_dataDir = Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"));
		_previousDataDir = Environment.GetEnvironmentVariable("MACRODECK_DATA_DIR");
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _dataDir);
		DatabaseMigrationHelper.MigrateDatabase(new MacroDeckPaths());

		_host = await new HostBuilder()
			.ConfigureWebHost(builder =>
			{
				builder.UseTestServer();
				builder.UseStartup<Startup>();
				builder.ConfigureTestServices(services =>
				{
					services.RemoveAll<IHostedService>();
					services.AddSingleton(Log.Logger);
					services.AddSingleton<ILogLevelState>(new LogLevelState(LogEntryLevel.Information));
					services.AddSingleton<IStartupFilter, FakeConnectionStartupFilter>();
					services.RemoveAll<StartupReadiness>();
					var readiness = new StartupReadiness();
					readiness.MarkCachesReady();
					readiness.MarkVariablesReady();
					services.AddSingleton(readiness);
				});
			})
			.StartAsync();
		_client = _host.GetTestClient();

		using var setup = new HttpRequestMessage(HttpMethod.Post, "/api/auth/setup");
		setup.Content = JsonContent.Create(new { username = "admin", password = "password123" });
		setup.Headers.Add(FakeConnectionStartupFilter.ShapeHeader, nameof(FakeConnectionShape.Loopback));
		Assert.That((await _client.SendAsync(setup)).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
	}

	[OneTimeTearDown]
	public async Task OneTimeTearDown()
	{
		_client.Dispose();
		await _host.StopAsync();
		_host.Dispose();
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _previousDataDir);
		if (Directory.Exists(_dataDir))
		{
			Directory.Delete(_dataDir, recursive: true);
		}
	}

	[Test]
	public async Task The_proof_signs_the_domain_line_the_key_the_local_endpoint_and_the_nonce()
	{
		var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

		var proof = await Prove(nonce, LocalIp);

		var expectedEndpoint = $"{LocalIp}:{HostEndpoints.PublicPort}";
		Assert.Multiple(() =>
		{
			Assert.That(proof.Endpoint, Is.EqualTo(expectedEndpoint));
			Assert.That(Convert.FromBase64String(proof.PublicKey), Has.Length.EqualTo(65));
			Assert.That(Verifies(proof, $"macrodeck-host-identity/v1\n{proof.PublicKey}\n{expectedEndpoint}\n{nonce}"),
				Is.True);
			Assert.That(Verifies(proof, $"macrodeck-host-identity/v1\n{proof.PublicKey}\n{expectedEndpoint}\nAAAA"),
				Is.False);
		});
	}

	[Test]
	public async Task A_client_supplied_endpoint_or_host_header_is_ignored()
	{
		using var request = Challenge(new { nonce = Nonce(), endpoint = "10.0.0.99:1" }, LocalIp);
		request.Headers.Host = "10.0.0.99:1";

		var proof = await ReadProof(await _client.SendAsync(request));

		Assert.That(proof.Endpoint, Is.EqualTo($"{LocalIp}:{HostEndpoints.PublicPort}"));
	}

	[TestCase("::ffff:192.168.1.10", "192.168.1.10")]
	[TestCase("fe80::1%4", "[fe80::1]")]
	[TestCase("2001:DB8:0:0:0:0:0:1", "[2001:db8::1]")]
	public async Task The_endpoint_is_signed_in_canonical_form(string socketAddress, string canonicalHost)
	{
		var proof = await Prove(Nonce(), socketAddress);

		Assert.That(proof.Endpoint, Is.EqualTo($"{canonicalHost}:{HostEndpoints.PublicPort}"));
	}

	[TestCase(null)]
	[TestCase("")]
	[TestCase("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8")]
	[TestCase("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=\n")]
	[TestCase("_-_-AwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=")]
	[TestCase("AAECAwQFBgcICQoLDA0O")]
	public async Task A_non_canonical_or_out_of_range_nonce_is_refused(string? nonce)
	{
		var response = await _client.SendAsync(Challenge(new { nonce }, LocalIp));

		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
	}

	[Test]
	public async Task Nonces_of_sixteen_and_sixty_four_bytes_are_accepted_and_sixty_five_is_not()
	{
		var sixteen = await _client.SendAsync(Challenge(new { nonce = Nonce(16) }, LocalIp));
		var sixtyFour = await _client.SendAsync(Challenge(new { nonce = Nonce(64) }, LocalIp));
		var sixtyFive = await _client.SendAsync(Challenge(new { nonce = Nonce(65) }, LocalIp));

		Assert.Multiple(() =>
		{
			Assert.That(sixteen.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(sixtyFour.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(sixtyFive.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
		});
	}

	[Test]
	public async Task More_than_thirty_challenges_in_ten_seconds_from_one_address_are_refused()
	{
		var remote = NextRemote();
		var statuses = new List<HttpStatusCode>();
		for (var i = 0; i < 31; i++)
		{
			statuses.Add((await _client.SendAsync(Challenge(new { nonce = Nonce() }, LocalIp, remote))).StatusCode);
		}

		var otherCaller = await _client.SendAsync(Challenge(new { nonce = Nonce() }, LocalIp));

		Assert.Multiple(() =>
		{
			Assert.That(statuses.Take(30), Has.All.EqualTo(HttpStatusCode.OK));
			Assert.That(statuses[30], Is.EqualTo(HttpStatusCode.TooManyRequests));
			Assert.That(otherCaller.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		});
	}

	[Test]
	public async Task Addresses_in_one_ipv6_64_share_the_challenge_limit()
	{
		var prefix = $"2001:db8:{Interlocked.Increment(ref _remoteCounter):x}:0";
		for (var i = 1; i <= 30; i++)
		{
			await _client.SendAsync(Challenge(new { nonce = Nonce() }, LocalIp, $"{prefix}::{i:x}"));
		}

		var sameNetwork = await _client.SendAsync(Challenge(new { nonce = Nonce() }, LocalIp, $"{prefix}::ffff"));

		Assert.That(sameNetwork.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
	}

	[TestCase("2001:db8:1:2::1", "2001:db8:1:2:ffff::9", true)]
	[TestCase("2001:db8:1:2::1", "2001:db8:1:3::1", false)]
	[TestCase("::ffff:192.168.1.50", "192.168.1.50", true)]
	[TestCase("192.168.1.50", "192.168.1.51", false)]
	public void Callers_are_limited_per_ipv4_address_or_ipv6_64(string first, string second, bool shared)
		=> Assert.That(HostIdentityRateLimit.PartitionKey(IPAddress.Parse(first)) ==
			HostIdentityRateLimit.PartitionKey(IPAddress.Parse(second)),
			Is.EqualTo(shared));

	[Test]
	public async Task Login_redeem_and_refresh_carry_the_key_the_challenge_proves()
	{
		var proof = await Prove(Nonce(), LocalIp);

		var login = await _client.PostAsJsonAsync("/api/auth/login",
			new { username = "admin", password = "password123", scope = "client" });
		var refreshCookie = login.Headers.GetValues("Set-Cookie")
			.First(cookie => cookie.StartsWith("md_refresh", StringComparison.Ordinal))
			.Split(';')[0];
		using var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
		refreshRequest.Headers.Add("Cookie", refreshCookie);
		var refresh = await _client.SendAsync(refreshRequest);

		using var mint = new HttpRequestMessage(HttpMethod.Post, "/api/auth/pairing-code");
		mint.Headers.Add(FakeConnectionStartupFilter.ShapeHeader, nameof(FakeConnectionShape.Loopback));
		var code = (await ReadJson(await _client.SendAsync(mint))).GetProperty("code").GetString();
		var redeem = await _client.PostAsJsonAsync("/api/auth/device-enrollment/redeem", new { token = code });

		Assert.Multiple(async () =>
		{
			Assert.That((await ReadJson(login)).GetProperty("hostKey").GetString(), Is.EqualTo(proof.PublicKey));
			Assert.That((await ReadJson(refresh)).GetProperty("hostKey").GetString(), Is.EqualTo(proof.PublicKey));
			Assert.That((await ReadJson(redeem)).GetProperty("hostKey").GetString(), Is.EqualTo(proof.PublicKey));
		});
	}

	private async Task<Proof> Prove(string nonce, string localIp)
		=> await ReadProof(await _client.SendAsync(Challenge(new { nonce }, localIp)));

	private static HttpRequestMessage Challenge(object body, string localIp, string? remoteIp = null)
	{
		var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/identity")
		{
			Content = JsonContent.Create(body)
		};
		request.Headers.Add(FakeConnectionStartupFilter.LocalIpHeader, localIp);
		request.Headers.Add(FakeConnectionStartupFilter.RemoteIpHeader, remoteIp ?? NextRemote());

		return request;
	}

	private static string NextRemote() => $"10.20.{Interlocked.Increment(ref _remoteCounter) / 250}.{_remoteCounter % 250 + 1}";

	private static string Nonce(int length = 32) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(length));

	private static async Task<Proof> ReadProof(HttpResponseMessage response)
	{
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		var json = await ReadJson(response);

		return new Proof(json.GetProperty("publicKey").GetString()!,
			json.GetProperty("endpoint").GetString()!,
			json.GetProperty("signature").GetString()!);
	}

	private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
		=> JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

	private static bool Verifies(Proof proof, string message)
	{
		var point = Convert.FromBase64String(proof.PublicKey);
		using var key = ECDsa.Create(new ECParameters
		{
			Curve = ECCurve.NamedCurves.nistP256,
			Q = new ECPoint { X = point[1..33], Y = point[33..] }
		});

		return key.VerifyData(Encoding.UTF8.GetBytes(message),
			Convert.FromBase64String(proof.Signature),
			HashAlgorithmName.SHA256,
			DSASignatureFormat.Rfc3279DerSequence);
	}

	private sealed record Proof(string PublicKey, string Endpoint, string Signature);
}
