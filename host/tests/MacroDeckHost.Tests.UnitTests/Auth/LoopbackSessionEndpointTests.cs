using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Auth;

[NonParallelizable]
public class LoopbackSessionEndpointTests
{
	private const string Nonce = "00112233445566778899aabbccddeeff";
	private IHost _host = null!;
	private HttpClient _client = null!;
	private string _dataDir = null!;
	private string? _previousDataDir;

	[SetUp]
	public async Task SetUp()
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
		var server = _host.GetTestServer();
		server.PreserveExecutionContext = true;
		_client = server.CreateClient();
	}

	[TearDown]
	public async Task TearDown()
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
	public async Task A_local_caller_without_the_secret_cannot_claim_setup_or_read_admin_state()
	{
		var setup = await Send(HttpMethod.Post,
			"/api/auth/setup",
			FakeConnectionShape.LoopbackWithoutCredential,
			JsonContent.Create(new { username = "admin", password = "password123" }));
		var profiles = await Send(HttpMethod.Get, "/api/profiles", FakeConnectionShape.LoopbackWithoutCredential);
		var status = await ReadJson(await Send(HttpMethod.Get,
			"/api/auth/status",
			FakeConnectionShape.LoopbackWithoutCredential));

		Assert.Multiple(() =>
		{
			Assert.That(setup.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(profiles.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			Assert.That(status.GetProperty("trusted").GetBoolean(), Is.False);
		});
	}

	[Test]
	public async Task The_desktop_app_holding_the_secret_is_trusted()
	{
		var status = await ReadJson(await Send(HttpMethod.Get, "/api/auth/status", FakeConnectionShape.Loopback));
		var setup = await Send(HttpMethod.Post,
			"/api/auth/setup",
			FakeConnectionShape.Loopback,
			JsonContent.Create(new { username = "admin", password = "password123" }));

		Assert.Multiple(() =>
		{
			Assert.That(status.GetProperty("trusted").GetBoolean(), Is.True);
			Assert.That(setup.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
		});
	}

	[Test]
	public async Task A_session_code_sets_a_strict_http_only_cookie_that_makes_the_window_trusted()
	{
		var response = await Send(HttpMethod.Get,
			$"/api/auth/loopback-session?code={NewCode()}&v=3.0.0-beta.7%2Bbuild.5",
			FakeConnectionShape.LoopbackWithoutCredential);
		var setCookie = response.Headers.GetValues("Set-Cookie").Single();
		var cookie = setCookie.Split(';')[0];

		var status = await ReadJson(await Send(HttpMethod.Get,
			"/api/auth/status",
			FakeConnectionShape.LoopbackWithoutCredential,
			cookie: cookie));

		Assert.Multiple(() =>
		{
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
			Assert.That(response.Headers.Location?.OriginalString, Is.EqualTo("/admin?v=3.0.0-beta.7%2Bbuild.5"));
			Assert.That(cookie, Does.StartWith($"md_loopback_{TestListenerPorts.Loopback}="));
			Assert.That(setCookie, Does.Contain("httponly").IgnoreCase);
			Assert.That(setCookie, Does.Contain("samesite=strict").IgnoreCase);
			Assert.That(status.GetProperty("trusted").GetBoolean(), Is.True);
		});
	}

	[Test]
	public async Task A_spent_expired_or_forged_code_redirects_to_the_app_without_a_cookie()
	{
		var code = NewCode();
		await Send(HttpMethod.Get, $"/api/auth/loopback-session?code={code}", FakeConnectionShape.LoopbackWithoutCredential);
		var expired = TestListenerPorts.SessionCode(TestListenerPorts.LoopbackSecret,
			DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5),
			NewNonce());
		var forged = TestListenerPorts.SessionCode(new string('a', 64), DateTimeOffset.UtcNow, NewNonce());

		foreach (var candidate in (string[])[code, expired, forged, "garbage"])
		{
			var response = await Send(HttpMethod.Get,
				$"/api/auth/loopback-session?code={Uri.EscapeDataString(candidate)}&v=<script>",
				FakeConnectionShape.LoopbackWithoutCredential);

			Assert.Multiple(() =>
			{
				Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), candidate);
				Assert.That(response.Headers.Location?.OriginalString, Is.EqualTo("/admin"), candidate);
				Assert.That(response.Headers.Contains("Set-Cookie"), Is.False, candidate);
			});
		}
	}

	[Test]
	public async Task The_session_and_proof_endpoints_do_not_exist_on_the_public_listener()
	{
		var session = await Send(HttpMethod.Get,
			$"/api/auth/loopback-session?code={NewCode()}",
			FakeConnectionShape.LoopbackOnPublicPort);
		var proof = await Send(HttpMethod.Post,
			"/api/auth/loopback-proof",
			FakeConnectionShape.PublicLan,
			JsonContent.Create(new { nonce = Nonce }));

		Assert.Multiple(() =>
		{
			Assert.That(session.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
			Assert.That(proof.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
		});
	}

	[Test]
	public async Task The_proof_answers_a_nonce_without_revealing_a_credential()
	{
		var response = await Send(HttpMethod.Post,
			"/api/auth/loopback-proof",
			FakeConnectionShape.LoopbackWithoutCredential,
			JsonContent.Create(new { nonce = Nonce }));
		var proof = (await ReadJson(response)).GetProperty("proof").GetString();
		var malformed = await Send(HttpMethod.Post,
			"/api/auth/loopback-proof",
			FakeConnectionShape.LoopbackWithoutCredential,
			JsonContent.Create(new { nonce = "session" }));

		Assert.Multiple(() =>
		{
			Assert.That(proof, Is.EqualTo(LoopbackSecret.Proof(Nonce)));
			Assert.That(proof, Is.Not.EqualTo(LoopbackSecret.SessionCookieValue()));
			Assert.That(malformed.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
		});
	}

	private static string NewNonce() => Convert.ToHexStringLower(Guid.NewGuid().ToByteArray());

	private static string NewCode()
		=> TestListenerPorts.SessionCode(TestListenerPorts.LoopbackSecret, DateTimeOffset.UtcNow, NewNonce());

	private async Task<HttpResponseMessage> Send(HttpMethod method,
		string path,
		FakeConnectionShape shape,
		HttpContent? content = null,
		string? cookie = null)
	{
		var request = new HttpRequestMessage(method, path) { Content = content };
		request.Headers.Add(FakeConnectionStartupFilter.ShapeHeader, shape.ToString());
		if (cookie is not null)
		{
			request.Headers.Add("Cookie", cookie);
		}

		return await _client.SendAsync(request);
	}

	private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
		=> JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
