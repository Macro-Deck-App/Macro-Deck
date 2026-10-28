using System.Net;
using System.Net.Http.Json;
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
public class LoginThrottleEndpointTests
{
	private const string Username = "admin";
	private const string Password = "password123";
	private const string Attacker = "203.0.113.7";

	private IHost _host = null!;
	private HttpClient _client = null!;
	private string _dataDir = null!;
	private string? _previousDataDir;

	[SetUp]
	public async Task StartHost()
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
		setup.Content = JsonContent.Create(new { username = Username, password = Password });
		setup.Headers.Add(FakeConnectionStartupFilter.ShapeHeader, nameof(FakeConnectionShape.Loopback));
		Assert.That((await _client.SendAsync(setup)).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
	}

	[TearDown]
	public async Task StopHost()
	{
		_client.Dispose();
		await _host.StopAsync();
		_host.Dispose();
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _previousDataDir);
		if (Directory.Exists(_dataDir))
		{
			Directory.Delete(_dataDir, true);
		}
	}

	[Test]
	public async Task Padding_or_recasing_the_username_does_not_buy_more_guesses()
	{
		foreach (var variant in new[] { "admin", "admin ", " admin", "ADMIN\t", "Admin  " })
		{
			using var failed = await Login(Attacker, variant, "wrong");
			Assert.That(failed.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
		}

		using var refused = await Login(Attacker, "admin   ", Password);

		Assert.Multiple(() =>
		{
			Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
			Assert.That(refused.Headers.RetryAfter?.Delta, Is.GreaterThan(TimeSpan.Zero));
		});
	}

	[Test]
	public async Task A_parallel_burst_gets_no_more_than_five_password_checks()
	{
		var responses = await Task.WhenAll(Enumerable.Range(0, 20)
			.Select(attempt => Login(Attacker, attempt % 2 == 0 ? Username : $"{Username} ", "wrong")));
		var statuses = responses.Select(response => response.StatusCode).ToList();
		foreach (var response in responses)
		{
			response.Dispose();
		}

		using var correctAfterwards = await Login(Attacker, Username, Password);
		using var otherSource = await Login("192.168.20.30", Username, Password);

		Assert.Multiple(() =>
		{
			Assert.That(statuses.Count(status => status == HttpStatusCode.Unauthorized), Is.EqualTo(5));
			Assert.That(statuses.Count(status => status == HttpStatusCode.TooManyRequests), Is.EqualTo(15));
			Assert.That(correctAfterwards.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
			Assert.That(otherSource.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		});
	}

	private async Task<HttpResponseMessage> Login(string remoteIp, string username, string password)
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login");
		request.Content = JsonContent.Create(new { username, password, scope = AuthDefaults.AdminScope });
		request.Headers.Add(FakeConnectionStartupFilter.RemoteIpHeader, remoteIp);
		return await _client.SendAsync(request);
	}
}
