using MacroDeckHost.Application.Announcements;
using MacroDeckHost.Application.Configuration;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Api;

[NonParallelizable]
public class AnnouncementEndpointsTests
{
	private IHost _host = null!;
	private HttpClient _client = null!;
	private string _dataDir = null!;
	private string? _previousDataDir;
	private readonly ScriptedPlatform _platform = new();

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
					services.AddSingleton<IStartupFilter, LoopbackStartupFilter>();
					services.RemoveAll<StartupReadiness>();
					services.AddSingleton(CompletedStartupReadiness());
					services.RemoveAll<IPlatformAnnouncementClient>();
					services.AddSingleton<IPlatformAnnouncementClient>(_platform);
				});
			})
			.StartAsync();

		_client = _host.GetTestClient();

		var setup = await _client.PostAsJsonAsync("/api/auth/setup",
			new { username = "admin", password = "password123" });
		Assert.That(setup.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
	}

	[OneTimeTearDown]
	public async Task OneTimeTearDown()
	{
		await _host.StopAsync();
		_client.Dispose();
		_host.Dispose();

		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _previousDataDir);
		if (Directory.Exists(_dataDir))
		{
			Directory.Delete(_dataDir, recursive: true);
		}
	}

	[Test]
	public async Task A_new_announcement_is_pending_until_it_is_marked_seen_and_then_stays_seen()
	{
		var announcements = _host.Services.GetRequiredService<IAnnouncementService>();
		_platform.Next = new AnnouncementFetch.Published(new Announcement(4,
			"Macro Deck 3 is here",
			"## What's new",
			DateTimeOffset.UtcNow.AddDays(-1),
			DateTimeOffset.UtcNow));
		await announcements.Refresh(default);

		var pending = await ReadJson(await _client.GetAsync("/api/announcements/pending"));
		var seen = await _client.PostAsJsonAsync("/api/announcements/seen", new { number = 4 });
		var afterwards = await ReadJson(await _client.GetAsync("/api/announcements/pending"));
		await announcements.Refresh(default);
		var refetched = await ReadJson(await _client.GetAsync("/api/announcements/pending"));

		Assert.Multiple(() =>
		{
			var announcement = pending.GetProperty("announcement");
			Assert.That(announcement.GetProperty("number").GetInt32(), Is.EqualTo(4));
			Assert.That(announcement.GetProperty("title").GetString(), Is.EqualTo("Macro Deck 3 is here"));
			Assert.That(announcement.GetProperty("content").GetString(), Is.EqualTo("## What's new"));
			Assert.That(seen.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(afterwards.GetProperty("announcement").ValueKind, Is.EqualTo(JsonValueKind.Null));
			Assert.That(refetched.GetProperty("announcement").ValueKind, Is.EqualTo(JsonValueKind.Null));
		});
	}

	private sealed class ScriptedPlatform : IPlatformAnnouncementClient
	{
		public AnnouncementFetch Next { get; set; } = new AnnouncementFetch.NonePublished();

		public Task<AnnouncementFetch> GetLatest(CancellationToken cancellationToken) => Task.FromResult(Next);
	}

	private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
	{
		var body = await response.Content.ReadAsStringAsync();
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);

		return JsonDocument.Parse(body).RootElement;
	}

	private static StartupReadiness CompletedStartupReadiness()
	{
		var readiness = new StartupReadiness();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();
		return readiness;
	}

	private sealed class LoopbackStartupFilter : IStartupFilter
	{
		public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
			=> app =>
			{
				app.Use(async (context, nextMiddleware) =>
				{
					context.Connection.LocalPort = TestListenerPorts.Loopback;
					context.Request.Headers[LoopbackSecret.HeaderName] = TestListenerPorts.LoopbackSecret;
					context.Connection.RemoteIpAddress = IPAddress.Loopback;
					await nextMiddleware();
				});
				next(app);
			};
	}
}
