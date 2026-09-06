using System.Net;
using System.Text.Json;
using MacroDeckHost.Application.Configuration;
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
public class SystemConnectionInfoEndpointTests
{
	private string _dataDir = null!;
	private string? _previousDataDir;

	[SetUp]
	public void SetUp()
	{
		_dataDir = Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"));
		_previousDataDir = Environment.GetEnvironmentVariable("MACRODECK_DATA_DIR");
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _dataDir);

		var paths = new MacroDeckPaths();
		DatabaseMigrationHelper.MigrateDatabase(paths);
	}

	[TearDown]
	public void TearDown()
	{
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _previousDataDir);
		if (Directory.Exists(_dataDir))
		{
			Directory.Delete(_dataDir, recursive: true);
		}
	}

	private const int PublicHttpPort = 9100;
	private const int PublicHttpsPort = 9101;

	[Test]
	public async Task Connection_info_offers_one_plain_http_endpoint_per_address()
	{
		var endpoints = await GetEndpoints(PublicEndpointSet.HttpOnly(PublicHttpPort));

		Assert.Multiple(() =>
		{
			Assert.That(endpoints.Select(e => e.Port), Is.All.EqualTo(PublicHttpPort));
			Assert.That(endpoints.Select(e => e.Ssl), Is.All.False);
		});
	}

	[Test]
	public async Task Connection_info_offers_only_https_when_it_replaced_the_http_listener()
	{
		var endpoints = await GetEndpoints(PublicEndpointSet.HttpsReplacingHttp(PublicHttpPort));

		Assert.Multiple(() =>
		{
			Assert.That(endpoints.Select(e => e.Ssl), Is.All.True);
			Assert.That(endpoints.Select(e => e.Port), Is.All.EqualTo(PublicHttpPort));
		});
	}

	// An existing device that reads the first entry keeps reaching a listener it can actually use: it
	// has not installed the local certificate authority yet on the upgrade that turns HTTPS on.
	[Test]
	public async Task Connection_info_offers_both_endpoints_per_address_with_http_first()
	{
		var endpoints = await GetEndpoints(PublicEndpointSet.HttpAndHttps(PublicHttpPort, PublicHttpsPort));

		foreach (var perAddress in endpoints.GroupBy(e => e.Address))
		{
			var ordered = perAddress.ToList();
			Assert.Multiple(() =>
			{
				Assert.That(ordered, Has.Count.EqualTo(2), perAddress.Key);
				Assert.That(ordered[0].Ssl, Is.False, perAddress.Key);
				Assert.That(ordered[0].Port, Is.EqualTo(PublicHttpPort), perAddress.Key);
				Assert.That(ordered[1].Ssl, Is.True, perAddress.Key);
				Assert.That(ordered[1].Port, Is.EqualTo(PublicHttpsPort), perAddress.Key);
			});
		}
	}

	// A listener that failed to open must not be advertised: a QR-code scanner would otherwise be
	// sent at a port that answers nothing (issue #515).
	[Test]
	public async Task A_listener_that_did_not_open_contributes_no_endpoints()
	{
		var endpoints = await GetEndpoints(
			PublicEndpointSet.HttpAndHttps(PublicHttpPort, PublicHttpsPort).WithoutHttps());

		Assert.Multiple(() =>
		{
			Assert.That(endpoints.Select(e => e.Ssl), Is.All.False);
			Assert.That(endpoints.Any(e => e.Port == PublicHttpsPort), Is.False);
		});
	}

	[TestCase(true, false)]
	[TestCase(false, true)]
	public async Task Connection_info_reports_whether_any_public_listener_is_serving(
		bool publicListenerAvailable,
		bool expectedUnavailable)
	{
		var configured = PublicEndpointSet.HttpOnly(PublicHttpPort);
		using var host = await StartHost(publicListenerAvailable ? configured : configured.WithoutHttp());
		using var client = host.GetTestClient();

		var response = await client.GetAsync("/api/system/connection-info");
		var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

		Assert.Multiple(() =>
		{
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(body.GetProperty("publicListenerUnavailable").GetBoolean(), Is.EqualTo(expectedUnavailable));
			if (!publicListenerAvailable)
			{
				Assert.That(body.GetProperty("endpoints").GetArrayLength(), Is.Zero);
			}
		});

		await host.StopAsync();
	}

	private static async Task<List<(string Address, int Port, bool Ssl)>> GetEndpoints(PublicEndpointSet configured)
	{
		using var host = await StartHost(configured);
		using var client = host.GetTestClient();

		var response = await client.GetAsync("/api/system/connection-info");
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

		var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
		var endpoints = body.GetProperty("endpoints")
			.EnumerateArray()
			.Select(e => (e.GetProperty("address").GetString()!,
				e.GetProperty("port").GetInt32(),
				e.GetProperty("ssl").GetBoolean()))
			.ToList();

		await host.StopAsync();

		Assume.That(endpoints, Is.Not.Empty, "no reachable IPv4 address on this machine");

		return endpoints;
	}

	private static async Task<IHost> StartHost(PublicEndpointSet publicEndpoints)
	{
		var listenerState = new FakeHostListenerState { PublicEndpoints = publicEndpoints };

		return await new HostBuilder()
			.ConfigureWebHost(builder =>
			{
				builder.UseTestServer();
				builder.UseStartup<Startup>();
				builder.ConfigureTestServices(services =>
				{
					services.RemoveAll<IHostedService>();
					services.AddSingleton(Log.Logger);
					services.AddSingleton<IStartupFilter, LoopbackStartupFilter>();
					services.RemoveAll<StartupReadiness>();
					services.AddSingleton(CompletedStartupReadiness());
					services.RemoveAll<IHostListenerState>();
					services.AddSingleton<IHostListenerState>(listenerState);
				});
			})
			.StartAsync();
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
