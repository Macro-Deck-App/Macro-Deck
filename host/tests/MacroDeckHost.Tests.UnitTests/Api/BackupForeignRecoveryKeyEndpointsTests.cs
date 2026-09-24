using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
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
public class BackupForeignRecoveryKeyEndpointsTests
{
	private IHost _host = null!;
	private HttpClient _client = null!;
	private string _dataDir = null!;
	private string? _previousDataDir;

	private string _foreignBackupId = null!;
	private string _foreignKey = null!;
	private string _currentKey = null!;
	private byte[] _foreignArchive = null!;

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
					services.AddSingleton<IStartupFilter, LoopbackStartupFilter>();
					services.RemoveAll<StartupReadiness>();
					services.AddSingleton(CompletedStartupReadiness());
				});
			})
			.StartAsync();

		_client = _host.GetTestClient();

		var created = await ReadJson(await Post("/api/backups/recovery-key", new { }));
		_foreignKey = created.GetProperty("key").GetString()!;

		var backup = await ReadJson(await Post("/api/backups", new { note = "foreign" }));
		Assert.That(backup.GetProperty("success").GetBoolean(), Is.True, backup.ToString());
		_foreignBackupId = backup.GetProperty("backup").GetProperty("id").GetString()!;
		_foreignArchive = await (await _client.SendAsync(Loopback(HttpMethod.Get,
			$"/api/backups/{_foreignBackupId}/download"))).Content.ReadAsByteArrayAsync();

		var regenerated = await ReadJson(await Post("/api/backups/recovery-key/regenerate",
			new { confirm = true, acknowledgeExistingBackupsBecomeUnreadable = true }));
		Assert.That(regenerated.GetProperty("success").GetBoolean(), Is.True, regenerated.ToString());
		_currentKey = regenerated.GetProperty("key").GetString()!;
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
	public async Task A_backup_from_another_recovery_key_asks_for_that_key()
	{
		var inspected = await Inspect(null);

		Assert.Multiple(() =>
		{
			Assert.That(inspected.GetProperty("success").GetBoolean(), Is.True, inspected.ToString());
			Assert.That(inspected.GetProperty("recoveryKeyRequired").GetBoolean(), Is.True);
			Assert.That(inspected.GetProperty("backup").GetProperty("decryptableLocally").GetBoolean(), Is.False);
		});
	}

	[Test]
	public async Task The_right_recovery_key_opens_the_backup()
	{
		var inspected = await Inspect(_foreignKey);

		Assert.Multiple(() =>
		{
			Assert.That(inspected.GetProperty("success").GetBoolean(), Is.True, inspected.ToString());
			Assert.That(inspected.GetProperty("components").EnumerateArray()
					.Sum(component => component.GetProperty("entryCount").GetInt32()),
				Is.GreaterThan(0),
				"the entries are only countable once the payload has been decrypted");
		});
	}

	[TestCase("not-a-recovery-key")]
	[TestCase("current")]
	public async Task A_wrong_recovery_key_is_reported_as_invalid(string key)
	{
		var inspected = await Inspect(key == "current" ? _currentKey : key);

		Assert.Multiple(() =>
		{
			Assert.That(inspected.GetProperty("success").GetBoolean(), Is.False);
			Assert.That(inspected.GetProperty("error").GetProperty("code").GetString(), Is.EqualTo("RecoveryKeyInvalid"));
		});
	}

	[Test]
	public async Task Importing_a_file_that_is_not_a_backup_reports_a_translatable_invalid_archive_error()
	{
		var imported = await Import(Encoding.UTF8.GetBytes("just some text"));

		Assert.Multiple(() =>
		{
			Assert.That(imported.GetProperty("success").GetBoolean(), Is.False);
			Assert.That(imported.GetProperty("error").GetProperty("code").GetString(), Is.EqualTo("InvalidArchive"));
			Assert.That(imported.GetProperty("error").GetProperty("message").GetProperty("$localized")
					.GetProperty("key").GetString(),
				Is.EqualTo("Errors.Backup.InvalidArchive"),
				"the client renders the message in the user's language");
		});
	}

	[Test]
	public async Task Importing_a_backup_that_is_already_listed_keeps_a_single_entry()
	{
		var imported = await Import(_foreignArchive);
		var listed = await ReadJson(await _client.SendAsync(Loopback(HttpMethod.Get, "/api/backups")));

		Assert.Multiple(() =>
		{
			Assert.That(imported.GetProperty("success").GetBoolean(), Is.True, imported.ToString());
			Assert.That(imported.GetProperty("backup").GetProperty("id").GetString(), Is.EqualTo(_foreignBackupId));
			Assert.That(listed.GetProperty("backups").EnumerateArray()
					.Count(backup => backup.GetProperty("id").GetString() == _foreignBackupId),
				Is.EqualTo(1));
		});
	}

	private async Task<JsonElement> Inspect(string? recoveryKey)
		=> await ReadJson(await Post($"/api/backups/{_foreignBackupId}/inspect", new { recoveryKey }));

	private async Task<JsonElement> Import(byte[] content)
	{
		using var request = Loopback(HttpMethod.Post, "/api/backups/import");
		var form = new MultipartFormDataContent();
		var file = new ByteArrayContent(content);
		file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
		form.Add(file, "file", "backup.macroDeckBackup");
		request.Content = form;

		return await ReadJson(await _client.SendAsync(request));
	}

	private async Task<HttpResponseMessage> Post(string path, object body)
	{
		using var request = Loopback(HttpMethod.Post, path);
		request.Content = JsonContent.Create(body);

		return await _client.SendAsync(request);
	}

	private static HttpRequestMessage Loopback(HttpMethod method, string path) => new(method, path);

	private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
		=> JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

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
