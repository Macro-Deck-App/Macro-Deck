using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Backups.Restore;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Api;

[NonParallelizable]
public class LegacyBackupRestoreEndpointsTests
{
	private const string LoopbackHeader = "X-Test-Loopback";

	private CultureInfo _culture = null!;
	private IHost? _host;
	private HttpClient _client = null!;
	private MacroDeckPaths _paths = null!;
	private string _dataDir = null!;
	private string? _previousDataDir;
	private CultureInfo _uiCulture = null!;

	[SetUp]
	public void SetUp()
	{
		_dataDir = Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"));
		_previousDataDir = Environment.GetEnvironmentVariable("MACRODECK_DATA_DIR");
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _dataDir);

		_culture = CultureInfo.CurrentCulture;
		_uiCulture = CultureInfo.CurrentUICulture;

		_paths = new MacroDeckPaths();
		_paths.EnsureDirectoriesExist();
		LegacyEvolveDatabase.Create(_paths.DatabasePath, LegacyEvolveDatabase.Newest);
	}

	[TearDown]
	public async Task TearDown()
	{
		await StopHost();
		CultureInfo.CurrentCulture = _culture;
		CultureInfo.CurrentUICulture = _uiCulture;
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _previousDataDir);
		SqliteConnection.ClearAllPools();

		if (Directory.Exists(_dataDir))
		{
			Directory.Delete(_dataDir, true);
		}
	}

	[Test]
	public async Task A_backup_taken_before_the_switch_restores_under_a_thai_regional_format()
	{
		CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("th-TH");
		CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("th-TH");
		await StartHost();
		await SetUpAccount("before-switch");
		var backupId = await CreateBackup();

		DatabaseMigrationHelper.MigrateDatabase(_paths);
		LegacyEvolveDatabase.Execute(_paths.DatabasePath, "UPDATE app_user SET u_username = 'after-switch';");

		var prepare = await PrepareRestore(backupId);
		Assert.That(prepare.GetProperty("success").GetBoolean(), Is.True, prepare.ToString());

		await StopHost();
		var outcome = StagedRestoreApplier.ApplyPending(_paths);
		DatabaseMigrationHelper.MigrateDatabase(_paths);

		Assert.Multiple(() =>
		{
			Assert.That(outcome, Is.EqualTo(StagedRestoreOutcome.Applied));
			Assert.That(LegacyEvolveDatabase.Query(_paths.DatabasePath, "SELECT u_username FROM app_user;"),
				Is.EqualTo(["before-switch"]));
			Assert.That(LegacyEvolveDatabase.Query(_paths.DatabasePath,
					$"SELECT \"MigrationId\" FROM \"{DatabaseMigrationHelper.HistoryTable}\" "
					+ "ORDER BY \"MigrationId\";"),
				Is.EqualTo(ShippedMigrations()));
		});
	}

	[Test]
	public async Task A_backup_from_a_newer_evolve_history_is_refused_as_an_unsupported_version()
	{
		LegacyEvolveDatabase.Record(_paths.DatabasePath,
			new Version(LegacyEvolveDatabase.Newest.Major, 999, 0, 0),
			true);
		await StartHost();
		await SetUpAccount("newer");
		var backupId = await CreateBackup();

		var prepare = await PrepareRestore(backupId);

		Assert.Multiple(() =>
		{
			Assert.That(prepare.GetProperty("success").GetBoolean(), Is.False);
			Assert.That(prepare.GetProperty("error").GetProperty("code").GetString(),
				Is.EqualTo(nameof(BackupError.UnsupportedVersion)));
		});
	}

	private async Task StartHost()
	{
		_host = await new HostBuilder()
			.ConfigureWebHost(builder =>
			{
				builder.UseTestServer(options => options.PreserveExecutionContext = true);
				builder.UseStartup<Startup>();
				builder.ConfigureTestServices(services =>
				{
					services.RemoveAll<IHostedService>();
					services.AddSingleton(Log.Logger);
					services.AddSingleton<IStartupFilter, LoopbackStartupFilter>();
					services.RemoveAll<StartupReadiness>();
					services.AddSingleton(CompletedStartupReadiness());
				});
			})
			.StartAsync();

		_client = _host.GetTestClient();
	}

	private async Task StopHost()
	{
		if (_host is null)
		{
			return;
		}

		_client.Dispose();
		await _host.StopAsync();
		_host.Dispose();
		_host = null;
	}

	private async Task SetUpAccount(string username)
	{
		var setup = await Send("/api/auth/setup", new { username, password = "password123" });
		Assert.That(setup.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
	}

	private async Task<string> CreateBackup()
	{
		var created = await ReadJson(await Send("/api/backups", new { note = "legacy" }));
		Assert.That(created.GetProperty("success").GetBoolean(), Is.True, created.ToString());

		return created.GetProperty("backup").GetProperty("id").GetString()!;
	}

	private async Task<JsonElement> PrepareRestore(string backupId)
	{
		return await ReadJson(await Send("/api/backups/restore/prepare",
			new { backupId, components = new[] { nameof(BackupComponentGroup.Accounts) } }));
	}

	private async Task<HttpResponseMessage> Send(string path, object body)
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, path);
		request.Content = JsonContent.Create(body);
		request.Headers.Add(LoopbackHeader, "1");

		return await _client.SendAsync(request);
	}

	private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
	{
		return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
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
		{
			return app =>
			{
				app.Use(async (context, nextMiddleware) =>
				{
					if (context.Request.Headers.ContainsKey(LoopbackHeader))
					{
						context.Connection.LocalPort = TestListenerPorts.Loopback;
						context.Request.Headers[LoopbackSecret.HeaderName] = TestListenerPorts.LoopbackSecret;
						context.Connection.RemoteIpAddress = IPAddress.Loopback;
					}

					await nextMiddleware();
				});
				next(app);
			};
		}
	}

	private List<string> ShippedMigrations()
	{
		using var context = new DatabaseContext(Log.Logger, Path.Combine(_dataDir, "shipped-migrations.db"));
		return [.. context.Database.GetMigrations().Order(StringComparer.Ordinal)];
	}
}
