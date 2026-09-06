using System.Net;
using System.Text.Json;
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
public class SystemBuildInfoEndpointTests
{
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

		var paths = new MacroDeckPaths();
		DatabaseMigrationHelper.MigrateDatabase(paths);

		_host = await new HostBuilder()
			.ConfigureWebHost(builder =>
			{
				builder.UseTestServer();
				builder.UseStartup<Startup>();
				builder.ConfigureTestServices(services =>
				{
					services.RemoveAll<IHostedService>();
					services.AddSingleton(Log.Logger);
					services.AddSingleton<IStartupFilter, PublicListenerStartupFilter>();
					services.RemoveAll<StartupReadiness>();
					services.AddSingleton(CompletedStartupReadiness());
				});
			})
			.StartAsync();

		_client = _host.GetTestClient();
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
	public async Task Build_info_is_served_without_authentication()
	{
		var response = await _client.GetAsync("/api/system/build-info");
		var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
		var commit = body.GetProperty("commit");

		Assert.Multiple(() =>
		{
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(commit.ValueKind == JsonValueKind.Null ? null : commit.GetString(),
				Is.EqualTo(HostBuildInfo.Commit));
		});
	}

	[Test]
	public async Task Build_info_is_never_cached()
	{
		var response = await _client.GetAsync("/api/system/build-info");

		Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
	}

	[Test]
	public async Task About_still_requires_authentication()
	{
		var response = await _client.GetAsync("/api/system/about");

		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
	}

	private static StartupReadiness CompletedStartupReadiness()
	{
		var readiness = new StartupReadiness();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();
		return readiness;
	}

	private sealed class PublicListenerStartupFilter : IStartupFilter
	{
		public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
			=> app =>
			{
				app.Use(async (context, nextMiddleware) =>
				{
					context.Connection.LocalPort = HostEndpoints.PublicPort;
					context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");
					await nextMiddleware();
				});
				next(app);
			};
	}
}
