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
public class OnboardingEndpointsTests
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

	// One scenario rather than several tests: what matters is the order a fresh account walks through,
	// and the flag is fixture-wide state that a second test would consume.
	[Test]
	public async Task An_account_is_owed_one_wizard_and_completing_it_clears_it_for_good()
	{
		var armed = await ReadJson(await _client.GetAsync("/api/settings/onboarding"));
		var completed = await ReadJson(await _client.PostAsync("/api/settings/onboarding/complete", null));
		var repeated = await ReadJson(await _client.PostAsync("/api/settings/onboarding/complete", null));
		var afterwards = await ReadJson(await _client.GetAsync("/api/settings/onboarding"));

		Assert.Multiple(() =>
		{
			Assert.That(armed.GetProperty("pending").GetBoolean(), Is.True);
			Assert.That(completed.GetProperty("pending").GetBoolean(), Is.False);
			Assert.That(repeated.GetProperty("pending").GetBoolean(), Is.False);
			Assert.That(afterwards.GetProperty("pending").GetBoolean(), Is.False);
		});
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
					context.Connection.RemoteIpAddress = IPAddress.Loopback;
					await nextMiddleware();
				});
				next(app);
			};
	}
}
