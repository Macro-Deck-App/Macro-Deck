using System.Net;
using System.Net.Http.Json;
using MacroDeck.Plugin.Protocol;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Usb;
using MacroDeckHost.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Auth;

[NonParallelizable]
public class ReverseProxyForwardingTests
{
	private const string Username = "admin";
	private const string Password = "password123";
	private const string Proxy = "127.0.0.1";
	private const string Attacker = "203.0.113.7";
	private const string Owner = "198.51.100.20";
	private const string BridgedHeader = "X-Test-Bridged";

	private IHost _host = null!;
	private HttpClient _client = null!;
	private string _dataDir = null!;
	private string? _previousDataDir;

	[SetUp]
	public async Task StartHost()
	{
		ReverseProxyTrust.Enabled = false;
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
					services.AddSingleton<IStartupFilter, BridgedConnectionStartupFilter>();
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
		ReverseProxyTrust.Enabled = false;
		_client.Dispose();
		await _host.StopAsync();
		_host.Dispose();
		SqliteConnection.ClearAllPools();
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _previousDataDir);
		if (Directory.Exists(_dataDir))
		{
			Directory.Delete(_dataDir, true);
		}
	}

	[TestCase(false)]
	[TestCase(true)]
	public async Task A_request_passed_on_by_a_proxy_is_never_a_local_plugin(bool trusted)
	{
		ReverseProxyTrust.Enabled = trusted;

		using var forwarded = await PluginPairing(Attacker);

		Assert.That(forwarded.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
	}

	[TestCase(false)]
	[TestCase(true)]
	public async Task A_local_plugin_still_reaches_the_pairing_endpoint(bool trusted)
	{
		ReverseProxyTrust.Enabled = trusted;

		using var local = await PluginPairing(null);

		Assert.That(local.StatusCode, Is.Not.EqualTo(HttpStatusCode.Forbidden));
	}

	[TestCase(Proxy)]
	[TestCase("::ffff:127.0.0.1")]
	public async Task With_the_setting_one_caller_behind_the_proxy_cannot_lock_out_another(string proxyAddress)
	{
		ReverseProxyTrust.Enabled = true;
		for (var i = 0; i < 5; i++)
		{
			using var failed = await Login(proxyAddress, Attacker, "wrong");
		}

		using var attacker = await Login(proxyAddress, Attacker, Password);
		using var owner = await Login(proxyAddress, Owner, Password);

		Assert.Multiple(() =>
		{
			Assert.That(attacker.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
			Assert.That(owner.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		});
	}

	[Test]
	public async Task Without_the_setting_forwarded_addresses_are_ignored()
	{
		for (var i = 0; i < 5; i++)
		{
			using var failed = await Login(Proxy, Attacker, "wrong");
		}

		using var owner = await Login(Proxy, Owner, Password);

		Assert.That(owner.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
	}

	[TestCase(true, true)]
	[TestCase(false, false)]
	public async Task A_login_through_an_https_proxy_gets_secure_cookies_only_with_the_setting(
		bool trusted,
		bool expectSecure)
	{
		ReverseProxyTrust.Enabled = trusted;

		using var login = await Login(Proxy, Owner, Password, "https");
		var cookies = login.Headers.GetValues("Set-Cookie").ToList();

		Assert.Multiple(() =>
		{
			Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(cookies.All(cookie => cookie.Contains("secure", StringComparison.OrdinalIgnoreCase)),
				Is.EqualTo(expectSecure));
		});
	}

	[Test]
	public async Task A_usb_bridged_device_cannot_claim_a_forwarded_address()
	{
		ReverseProxyTrust.Enabled = true;
		for (var i = 0; i < 5; i++)
		{
			using var failed = await Login(Proxy, Attacker, "wrong", null, true);
		}

		using var again = await Login(Proxy, Owner, Password, null, true);

		Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
	}

	[Test]
	public async Task The_loopback_listener_ignores_forwarded_addresses()
	{
		ReverseProxyTrust.Enabled = true;
		for (var i = 0; i < 5; i++)
		{
			using var failed = await Login(null, Attacker, "wrong", null, false, true);
		}

		using var again = await Login(null, Owner, Password, null, false, true);

		Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
	}

	private async Task<HttpResponseMessage> PluginPairing(string? forwardedFor)
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, ProtocolConstants.PairingPath);
		request.Content = JsonContent.Create(new { });
		request.Headers.Add(FakeConnectionStartupFilter.RemoteIpHeader, Proxy);
		if (forwardedFor is not null)
		{
			request.Headers.Add("X-Forwarded-For", forwardedFor);
		}

		return await _client.SendAsync(request);
	}

	private async Task<HttpResponseMessage> Login(
		string? peer,
		string forwardedFor,
		string password,
		string? forwardedProto = null,
		bool bridged = false,
		bool loopbackListener = false)
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login");
		request.Content = JsonContent.Create(new { username = Username, password, scope = AuthDefaults.AdminScope });
		if (loopbackListener)
		{
			request.Headers.Add(FakeConnectionStartupFilter.ShapeHeader,
				nameof(FakeConnectionShape.LoopbackWithoutCredential));
		}

		if (peer is not null)
		{
			request.Headers.Add(FakeConnectionStartupFilter.RemoteIpHeader, peer);
		}

		request.Headers.Add("X-Forwarded-For", forwardedFor);
		if (forwardedProto is not null)
		{
			request.Headers.Add("X-Forwarded-Proto", forwardedProto);
		}

		if (bridged)
		{
			request.Headers.Add(BridgedHeader, "usb:SERIAL1234");
		}

		return await _client.SendAsync(request);
	}

	private sealed class BridgedConnectionStartupFilter : IStartupFilter
	{
		public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
		{
			return app =>
			{
				app.Use(async (context, nextMiddleware) =>
				{
					if (context.Request.Headers.TryGetValue(BridgedHeader, out var deviceKey))
					{
						context.Features.Set<IBridgedConnectionFeature>(new BridgedConnectionFeature(deviceKey!));
					}

					await nextMiddleware();
				});
				next(app);
			};
		}
	}
}
