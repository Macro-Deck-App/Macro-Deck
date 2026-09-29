using System.Net;
using System.Net.Http.Json;
using System.Text;
using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Ui;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Host;

[NonParallelizable]
public class InstallationIntegrityEndpointTests
{
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
		setup.Content = JsonContent.Create(new { username = "admin", password = "password123" });
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
			Directory.Delete(_dataDir, recursive: true);
		}
	}

	private const string DamagedBody = """
		{"status":"damaged","reason":"files","missing":1,"modified":0,"installKind":"windows"}
		""";

	[Test]
	public async Task The_bootstrapper_on_loopback_gets_an_acknowledgement_and_the_user_a_notification()
	{
		var response = await PostIntegrity(FakeConnectionShape.Loopback);

		Assert.Multiple(async () =>
		{
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("""{"accepted":true}"""));
			Assert.That(DamageNotifications().Count(), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task A_device_on_the_network_cannot_raise_the_notice()
	{
		var response = await PostIntegrity(FakeConnectionShape.PublicLan);

		Assert.Multiple(() =>
		{
			Assert.That(response.IsSuccessStatusCode, Is.False);
			Assert.That(DamageNotifications(), Is.Empty);
		});
	}

	private async Task<HttpResponseMessage> PostIntegrity(FakeConnectionShape shape)
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, "/api/host/installation-integrity");
		request.Content = new StringContent(DamagedBody, Encoding.UTF8, "application/json");
		request.Headers.Add(FakeConnectionStartupFilter.ShapeHeader, shape.ToString());
		return await _client.SendAsync(request);
	}

	private IEnumerable<UserNotification> DamageNotifications()
		=> _host.Services.GetRequiredService<IUserNotificationStore>()
			.Snapshot()
			.Where(n => n.Title == "Macro Deck was not updated completely");
}
