using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MacroDeckHost.Application.Auth;
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
public class PasswordResetSessionTests
{
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
		_client = _host.GetTestClient();

		var setup = await Send(HttpMethod.Post,
			"/api/auth/setup",
			new { username = "admin", password = "password123" },
			loopback: true);
		Assert.That(setup.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
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
	public async Task A_reset_ends_every_pre_reset_session_and_a_fresh_login_on_the_same_device_works()
	{
		var before = await Login("password123", deviceId: null, deviceSecret: null);
		var beforeBody = await ReadJson(before);
		var oldToken = beforeBody.GetProperty("accessToken").GetString()!;
		var deviceId = beforeBody.GetProperty("device").GetProperty("deviceId").GetString()!;
		var deviceSecret = beforeBody.GetProperty("device").GetProperty("deviceSecret").GetString()!;
		var oldRefreshCookie = RefreshCookie(before);
		var ticketBefore = await MintTicket(oldToken);

		var reset = await Send(HttpMethod.Post,
			"/api/auth/reset-password",
			new { newPassword = "newpassword1" },
			loopback: true);

		var staleRead = await Send(HttpMethod.Get, "/api/folders", bearerToken: oldToken);
		var staleTicket = await MintTicket(oldToken);
		var staleRefresh = await Send(HttpMethod.Post, "/api/auth/refresh", cookie: oldRefreshCookie);
		var oldPassword = await Login("password123", deviceId, deviceSecret);
		var fresh = await Login("newpassword1", deviceId, deviceSecret);
		var freshBody = await ReadJson(fresh);
		var freshTicket = await MintTicket(freshBody.GetProperty("accessToken").GetString()!);

		Assert.Multiple(() =>
		{
			Assert.That(ticketBefore.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(reset.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
			Assert.That(staleRead.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			Assert.That(staleTicket.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			Assert.That(staleRefresh.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			Assert.That(oldPassword.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			Assert.That(fresh.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(freshBody.GetProperty("device").GetProperty("deviceId").GetString(), Is.EqualTo(deviceId));
			Assert.That(freshTicket.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		});
	}

	[Test]
	public async Task The_new_password_is_not_locked_out_by_failed_attempts_with_the_forgotten_one()
	{
		for (var attempt = 0; attempt < 8; attempt++)
		{
			await Login("forgotten-password", deviceId: null, deviceSecret: null);
		}

		var throttled = await Login("password123", deviceId: null, deviceSecret: null);
		await Send(HttpMethod.Post, "/api/auth/reset-password", new { newPassword = "newpassword1" }, loopback: true);
		var afterReset = await Login("newpassword1", deviceId: null, deviceSecret: null);

		Assert.Multiple(() =>
		{
			Assert.That(throttled.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
			Assert.That(afterReset.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		});
	}

	private Task<HttpResponseMessage> Login(string password, string? deviceId, string? deviceSecret)
		=> Send(HttpMethod.Post,
			"/api/auth/login",
			new
			{
				username = "admin",
				password,
				scope = "client",
				device = new
				{
					deviceId,
					deviceSecret,
					clientType = "web-client",
					proposedName = "Tablet",
					platform = "Android",
					browser = "Chrome",
					formFactor = "tablet",
					appVersion = "test"
				}
			});

	private async Task<HttpResponseMessage> MintTicket(string bearerToken)
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, "/api/ui-websocket/tickets")
		{
			Content = JsonContent.Create(new { })
		};
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
		request.Headers.Add("X-MacroDeck-Ui-Protocol", "1");
		return await _client.SendAsync(request);
	}

	private async Task<HttpResponseMessage> Send(
		HttpMethod method,
		string path,
		object? body = null,
		string? bearerToken = null,
		bool loopback = false,
		string? cookie = null)
	{
		using var request = new HttpRequestMessage(method, path);
		if (body is not null)
		{
			request.Content = JsonContent.Create(body);
		}

		if (bearerToken is not null)
		{
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
		}

		if (loopback)
		{
			request.Headers.Add(FakeConnectionStartupFilter.ShapeHeader, nameof(FakeConnectionShape.Loopback));
		}

		if (cookie is not null)
		{
			request.Headers.Add("Cookie", cookie);
		}

		return await _client.SendAsync(request);
	}

	private static string RefreshCookie(HttpResponseMessage response)
		=> response.Headers.GetValues("Set-Cookie")
			.First(value => value.StartsWith(AuthDefaults.RefreshCookie + "_", StringComparison.Ordinal))
			.Split(';')[0];

	private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
		=> JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
