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
public class UpdateStateEndpointTests
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

	[Test]
	public async Task A_failed_update_check_without_a_version_is_accepted_and_notifies_the_user()
	{
		var response = await PostUpdateState(CheckFailedBody);

		var update = UpdateNotifications().SingleOrDefault();
		Assert.Multiple(() =>
		{
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(update, Is.Not.Null);
			Assert.That(update?.Severity, Is.EqualTo(UserNotificationSeverity.Warning));
			Assert.That(update?.Title, Is.EqualTo("Could not check for updates"));
			Assert.That(update?.Message, Is.EqualTo(CheckFailedError));
		});
	}

	[Test]
	public async Task An_up_to_date_report_without_a_version_is_accepted_and_clears_the_failure_warning()
	{
		var failed = await PostUpdateState(CheckFailedBody);
		var upToDate = await PostUpdateState("""
			{"version":null,"phase":"upToDate","publishedAt":null,"downloaded":null,"total":null,"percent":null,"error":null,"canInstall":false}
			""");

		Assert.Multiple(() =>
		{
			Assert.That(failed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(upToDate.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(UpdateNotifications(), Is.Empty);
		});
	}

	[Test]
	public async Task A_failed_check_reported_with_its_failure_kind_and_a_known_version_is_accepted()
	{
		var response = await PostUpdateState($$"""
			{"version":"3.1.0","phase":"failed","publishedAt":null,"downloaded":null,"total":null,"percent":null,"error":"{{CheckFailedError}}","failure":"check","canInstall":true}
			""");

		Assert.Multiple(() =>
		{
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(UpdateNotifications().Select(n => n.Title), Is.EqualTo(new[] { "Could not check for updates" }));
		});
	}

	private const string CheckFailedError
		= "could not check for updates on any feed (https://updates.example/stable.json: connection refused)";

	private const string CheckFailedBody = $$"""
		{"version":null,"phase":"failed","publishedAt":null,"downloaded":null,"total":null,"percent":null,"error":"{{CheckFailedError}}","canInstall":false}
		""";

	private async Task<HttpResponseMessage> PostUpdateState(string body)
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, "/api/host/update-state");
		request.Content = new StringContent(body, Encoding.UTF8, "application/json");
		request.Headers.Add(FakeConnectionStartupFilter.ShapeHeader, nameof(FakeConnectionShape.Loopback));
		return await _client.SendAsync(request);
	}

	private IEnumerable<UserNotification> UpdateNotifications()
		=> _host.Services.GetRequiredService<IUserNotificationStore>()
			.Snapshot()
			.Where(n => n.Kind == UserNotificationKind.Update);
}
