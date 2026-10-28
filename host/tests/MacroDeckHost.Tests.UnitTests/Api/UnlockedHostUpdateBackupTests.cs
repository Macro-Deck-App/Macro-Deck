using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Tests.UnitTests.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Api;

[NonParallelizable]
public class UnlockedHostUpdateBackupTests
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
					services.AddSingleton<IStartupFilter, FakeConnectionStartupFilter>();
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
			Directory.Delete(_dataDir, true);
		}
	}

	[Test]
	public async Task An_update_on_a_readable_key_ring_is_not_let_through_without_its_backup()
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, "/api/backups/before-host-update");
		request.Content = JsonContent.Create(new { version = "3.0.1" });
		request.Headers.Add(FakeConnectionStartupFilter.ShapeHeader, nameof(FakeConnectionShape.Loopback));

		var response = await _client.SendAsync(request);
		var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

		Assert.Multiple(() =>
		{
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(body.GetProperty("skipped").GetBoolean(), Is.False);
			Assert.That(body.GetProperty("reason").GetString(), Is.Not.EqualTo("KeyRingLocked"));
		});
	}
}
