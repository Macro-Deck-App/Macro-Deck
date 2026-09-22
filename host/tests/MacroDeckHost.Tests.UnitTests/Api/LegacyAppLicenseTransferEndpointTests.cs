using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using MacroDeckHost.Application.Licensing;
using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Infrastructure.Licensing;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Licensing;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.Licensing;
using MacroDeckHost.Ui;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Api;

[NonParallelizable]
public class LegacyAppLicenseTransferEndpointTests
{
	private const string TransferPath = "/api/legacy/md2-app/license-transfer";
	private const string KeyId = "prod-test-md2";

	private static readonly ECDsa ProductionKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

	private CompanionLicenseServiceTests.ScriptedPlatform _platform = null!;
	private IHost _host = null!;
	private HttpClient _client = null!;
	private string _dataDir = null!;
	private string? _previousDataDir;

	[OneTimeTearDown]
	public static void DisposeKey() => ProductionKey.Dispose();

	[SetUp]
	public async Task SetUp()
	{
		_dataDir = Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"));
		_previousDataDir = Environment.GetEnvironmentVariable("MACRODECK_DATA_DIR");
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _dataDir);
		DatabaseMigrationHelper.MigrateDatabase(new MacroDeckPaths());

		var platform = _platform = new CompanionLicenseServiceTests.ScriptedPlatform();
		var parameters = ProductionKey.ExportParameters(false);
		var trustedKeys = new Dictionary<string, string>
		{
			[KeyId] = Convert.ToBase64String([0x04, .. parameters.Q.X!, .. parameters.Q.Y!])
		};
		_host = await new HostBuilder()
			.ConfigureWebHost(builder =>
			{
				builder.UseTestServer();
				builder.UseStartup<Startup>();
				builder.ConfigureTestServices(services =>
				{
					services.RemoveAll<IHostedService>();
					services.AddHostedService<CompanionLicenseBackgroundService>();
					services.RemoveAll<IPlatformLicenseClient>();
					services.AddSingleton<IPlatformLicenseClient>(platform);
					services.RemoveAll<CompanionLicenseTokens>();
					services.AddSingleton(new CompanionLicenseTokens(trustedKeys));
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
	}

	[TearDown]
	public async Task TearDown()
	{
		_client.Dispose();
		await _host.StopAsync();
		_host.Dispose();
		SqliteConnection.ClearAllPools();
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _previousDataDir);
		if (Directory.Exists(_dataDir))
		{
			Directory.Delete(_dataDir, recursive: true);
		}
	}

	[Test]
	public async Task An_anonymous_transfer_is_answered_with_a_status_and_a_code()
	{
		var license = CompanionLicenseTokens.Sign(ProductionKey, KeyId, "license-md2", "app-store-legacy",
			DateTimeOffset.UtcNow);
		_platform.Answer = _ => new PlatformLicenseIssueResult.Issued(license);

		var response = await Send(Body(AppTransaction("com.suchbyte.macrodeck")));

		var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
		Assert.Multiple(() =>
		{
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(answer.GetProperty("status").GetString(), Is.EqualTo("transferred"));
			Assert.That(answer.GetProperty("code").ValueKind, Is.EqualTo(JsonValueKind.Null));
			Assert.That(_platform.Proofs.Single().Platform, Is.EqualTo("app-store-legacy"));
		});
	}

	[Test]
	public async Task A_host_that_holds_a_license_answers_already_transferred()
	{
		var license = CompanionLicenseTokens.Sign(ProductionKey, KeyId, "license-md2", "app-store-legacy",
			DateTimeOffset.UtcNow);
		_platform.Answer = _ => new PlatformLicenseIssueResult.Issued(license);
		await Send(Body(AppTransaction("com.suchbyte.macrodeck")));

		var response = await Send(Body(AppTransaction("com.suchbyte.macrodeck", "app-transaction-2")));

		var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
		Assert.Multiple(() =>
		{
			Assert.That(answer.GetProperty("status").GetString(), Is.EqualTo("alreadyTransferred"));
			Assert.That(_platform.Proofs, Has.Count.EqualTo(1));
		});
	}

	[TestCase("app-store", "appTransaction", "com.suchbyte.macrodeck", "unsupported-source")]
	[TestCase("app-store-legacy", "receipt", "com.suchbyte.macrodeck", "unsupported-source")]
	[TestCase("app-store-legacy", "appTransaction", null, "invalid-proof")]
	[TestCase("app-store-legacy", "appTransaction", "app.macrodeck.companion", "bundle-mismatch")]
	public async Task A_proof_that_cannot_be_the_macro_deck_2_purchase_is_rejected_without_asking_the_platform(
		string platform,
		string legacyKind,
		string? bundleId,
		string code)
	{
		var signedPayload = bundleId is null ? "not-a-jws" : AppTransaction(bundleId);

		var response = await Send(new { platform, legacyKind, signedPayload });

		var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
		Assert.Multiple(() =>
		{
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(answer.GetProperty("status").GetString(), Is.EqualTo("rejected"));
			Assert.That(answer.GetProperty("code").GetString(), Is.EqualTo(code));
			Assert.That(_platform.Proofs, Is.Empty);
		});
	}

	[Test]
	public async Task The_endpoint_answers_cross_origin_like_the_other_anonymous_endpoints()
	{
		using var request = Request(Body("not-a-jws"));
		request.Headers.Add("Origin", "http://phone.local");

		var response = await _client.SendAsync(request);

		Assert.That(response.Headers.GetValues("Access-Control-Allow-Origin"), Is.EqualTo(new[] { "http://phone.local" }));
	}

	[Test]
	public async Task More_than_five_transfers_a_minute_from_one_address_are_refused()
	{
		var statuses = new List<HttpStatusCode>();
		for (var i = 0; i <= LegacyLicenseTransferRateLimit.PerAddressLimit; i++)
		{
			statuses.Add((await Send(Body("not-a-jws"), "192.168.1.60")).StatusCode);
		}

		var otherCaller = await Send(Body("not-a-jws"), "192.168.1.61");

		Assert.Multiple(() =>
		{
			Assert.That(statuses.Take(LegacyLicenseTransferRateLimit.PerAddressLimit), Has.All.EqualTo(HttpStatusCode.OK));
			Assert.That(statuses[^1], Is.EqualTo(HttpStatusCode.TooManyRequests));
			Assert.That(otherCaller.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		});
	}

	[Test]
	public async Task Callers_rotating_through_addresses_hit_the_host_wide_ceiling()
	{
		var statuses = new List<HttpStatusCode>();
		for (var i = 1; i <= LegacyLicenseTransferRateLimit.HostLimit + 1; i++)
		{
			statuses.Add((await Send(Body("not-a-jws"), $"10.0.0.{i}")).StatusCode);
		}

		Assert.Multiple(() =>
		{
			Assert.That(statuses.Take(LegacyLicenseTransferRateLimit.HostLimit), Has.All.EqualTo(HttpStatusCode.OK));
			Assert.That(statuses[^1], Is.EqualTo(HttpStatusCode.TooManyRequests));
		});
	}

	private Task<HttpResponseMessage> Send(object body, string? remoteIp = null)
		=> _client.SendAsync(Request(body, remoteIp));

	private static HttpRequestMessage Request(object body, string? remoteIp = null)
	{
		var request = new HttpRequestMessage(HttpMethod.Post, TransferPath) { Content = JsonContent.Create(body) };
		if (remoteIp is not null)
		{
			request.Headers.Add(FakeConnectionStartupFilter.RemoteIpHeader, remoteIp);
		}

		return request;
	}

	private static object Body(string signedPayload)
		=> new { platform = "app-store-legacy", legacyKind = "appTransaction", signedPayload };

	private static string AppTransaction(string bundleId, string appTransactionId = "app-transaction-1")
		=> $"eyJhbGciOiJFUzI1NiJ9.{Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { appTransactionId, bundleId }))}.c2lnbmF0dXJl";

	private static string Base64Url(byte[] bytes)
		=> Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
