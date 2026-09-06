using System.Net;
using System.Net.Http.Json;
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
public class IconPackPathEndpointTests
{
	private const string LoopbackHeader = "X-Test-Loopback";

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
					services.AddSingleton<IStartupFilter, TrustedLoopbackStartupFilter>();
					services.RemoveAll<StartupReadiness>();
					services.AddSingleton(CompletedStartupReadiness());
				});
			})
			.StartAsync();

		_client = _host.GetTestClient();

		var setup = await SendJson(HttpMethod.Post,
			"/api/auth/setup",
			new { username = "admin", password = "password123" });
		Assert.That(setup.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
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
	public async Task Restoring_a_pack_from_a_path_creates_a_new_pack_rather_than_merging()
	{
		var packId = await CreatePack("Neon");
		var path = await ExportPackToDisk(packId, "neon.macroDeckIconPack");

		var first = await ReadJson(await SendJson(HttpMethod.Post,
			"/api/icon-packs/restore-from-path",
			new { path }));
		var second = await ReadJson(await SendJson(HttpMethod.Post,
			"/api/icon-packs/restore-from-path",
			new { path }));

		var firstId = first.GetProperty("packs")[0].GetProperty("id").GetString();
		var secondId = second.GetProperty("packs")[0].GetProperty("id").GetString();

		Assert.Multiple(() =>
		{
			Assert.That(first.GetProperty("success").GetBoolean(), Is.True);
			Assert.That(second.GetProperty("success").GetBoolean(), Is.True);
			Assert.That(firstId, Is.Not.EqualTo(packId), "restore must not reuse the source pack id");
			Assert.That(secondId, Is.Not.EqualTo(firstId));
		});
	}

	[Test]
	public async Task Restoring_from_a_path_rejects_a_file_that_is_not_a_pack()
	{
		var path = Path.Combine(_dataDir, "not-a-pack.zip");
		await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);

		var body = await ReadJson(await SendJson(HttpMethod.Post,
			"/api/icon-packs/restore-from-path",
			new { path }));

		Assert.Multiple(() =>
		{
			Assert.That(body.GetProperty("success").GetBoolean(), Is.False);
			Assert.That(body.GetProperty("error").GetProperty("code").GetString(), Is.EqualTo("ValidationError"));
		});
	}

	[Test]
	public async Task Restoring_from_a_path_rejects_a_file_that_does_not_exist()
	{
		var path = Path.Combine(_dataDir, "absent.macroDeckIconPack");

		var body = await ReadJson(await SendJson(HttpMethod.Post,
			"/api/icon-packs/restore-from-path",
			new { path }));

		Assert.That(body.GetProperty("error").GetProperty("code").GetString(), Is.EqualTo("NotFound"));
	}

	private async Task<string> CreatePack(string name)
	{
		var response = await SendJson(HttpMethod.Post, "/api/icon-packs", new { name });
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "create pack");
		var json = await ReadJson(response);
		return json.GetProperty("pack").GetProperty("id").GetString()!;
	}

	private async Task<string> ExportPackToDisk(string packId, string fileName)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/icon-packs/{packId}/export");
		request.Headers.Add(LoopbackHeader, "1");
		var response = await _client.SendAsync(request);
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "export pack");

		var directory = Path.Combine(_dataDir, "opened-files");
		Directory.CreateDirectory(directory);
		var path = Path.Combine(directory, fileName);
		await File.WriteAllBytesAsync(path, await response.Content.ReadAsByteArrayAsync());
		return path;
	}

	private async Task<HttpResponseMessage> SendJson(HttpMethod method, string path, object body)
	{
		using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
		request.Headers.Add(LoopbackHeader, "1");
		return await _client.SendAsync(request);
	}

	private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
		=> JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

	private static StartupReadiness CompletedStartupReadiness()
	{
		var readiness = new StartupReadiness();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();
		return readiness;
	}

	private sealed class TrustedLoopbackStartupFilter : IStartupFilter
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
