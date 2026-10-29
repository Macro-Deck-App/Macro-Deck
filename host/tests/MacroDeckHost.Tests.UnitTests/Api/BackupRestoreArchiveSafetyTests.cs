using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MacroDeck.Localization.Serialization;
using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Backups;
using MacroDeckHost.Infrastructure.Backups.Crypto;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Api;

[NonParallelizable]
public class BackupRestoreArchiveSafetyTests
{
	private const string LoopbackHeader = "X-Test-Loopback";
	private const string OwnedFile = "data/icons/md04/original.bin";
	private const string EscapeFileName = "zip-slip.txt";

	private static readonly byte[] _originalContent = Encoding.UTF8.GetBytes("original icon bytes");

	private IHost _host = null!;
	private HttpClient _client = null!;
	private IMacroDeckPaths _paths = null!;
	private string _testRoot = null!;
	private string _escapeDirectory = null!;
	private string? _previousDataDir;
	private byte[] _sourceArchive = null!;
	private List<string> _components = null!;

	[OneTimeSetUp]
	public async Task OneTimeSetUp()
	{
		_testRoot = Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"));
		var dataDir = Path.Combine(_testRoot, "data-root");
		_escapeDirectory = Path.Combine(_testRoot, "outside");
		_previousDataDir = Environment.GetEnvironmentVariable("MACRODECK_DATA_DIR");
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", dataDir);

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
		_paths = _host.Services.GetRequiredService<IMacroDeckPaths>();

		var setup = await Send(HttpMethod.Post,
			"/api/auth/setup",
			new { username = "admin", password = "password123" });
		Assert.That(setup.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

		var owned = DataPath(OwnedFile);
		Directory.CreateDirectory(Path.GetDirectoryName(owned)!);
		File.WriteAllBytes(owned, _originalContent);

		var created = await ReadJson(await Send(HttpMethod.Post, "/api/backups", new { note = "archive safety" }));
		Assert.That(created.GetProperty("success").GetBoolean(), Is.True, created.ToString());
		var backup = created.GetProperty("backup");
		_components = [.. backup.GetProperty("components").EnumerateArray().Select(value => value.GetString()!)];

		var download = await Send(HttpMethod.Get, $"/api/backups/{backup.GetProperty("id").GetString()}/download");
		_sourceArchive = await download.Content.ReadAsByteArrayAsync();
	}

	[OneTimeTearDown]
	public async Task OneTimeTearDown()
	{
		_client.Dispose();
		await _host.StopAsync();
		_host.Dispose();
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _previousDataDir);

		if (Directory.Exists(_testRoot))
		{
			Directory.Delete(_testRoot, true);
		}
	}

	[TearDown]
	public void TearDown()
	{
		if (Directory.Exists(_escapeDirectory))
		{
			Directory.Delete(_escapeDirectory, true);
		}
	}

	private static IEnumerable<TestCaseData> UnsafeArchives()
	{
		yield return Case("a parent-directory path under an owned folder",
			"InvalidArchive",
			test => [Entry(test.TraversalFrom("data/icons/md04/", '/'), BackupComponentGroup.Icons)]);
		yield return Case("an absolute path",
			"InvalidArchive",
			test => [Entry(test.EscapeTarget.Replace('\\', '/'), BackupComponentGroup.Icons)]);
		yield return Case("a parent-directory path written with backslashes",
			"InvalidArchive",
			test => [Entry(test.TraversalFrom("data\\icons\\md04\\", '\\'), BackupComponentGroup.Icons)]);
		yield return Case("a drive-qualified path",
			"InvalidArchive",
			_ => [Entry("C:/md04/" + EscapeFileName, BackupComponentGroup.Icons)]);
		yield return Case("a file tagged with a group that does not own it",
			"InvalidArchive",
			_ => [Entry("data/scripts/md04.json", BackupComponentGroup.Icons)]);
		yield return Case("a path the backup never archives",
			"InvalidArchive",
			_ => [Entry("plugins/_staging/md04/payload.dll", BackupComponentGroup.Plugins)]);
		yield return Case("the same file twice",
			"InvalidArchive",
			_ => [Entry(OwnedFile, BackupComponentGroup.Icons)]);
		yield return Case("a file larger than a single entry may be",
			"TooLarge",
			_ =>
			[
				Entry("data/icons/md04/huge.bin", BackupComponentGroup.Icons) with
				{
					DeclaredSize = BackupArchiveLimits.MaxSingleEntryBytes + 1
				}
			]);
		yield return Case("files that together expand beyond a whole backup",
			"TooLarge",
			_ =>
			[
				.. Enumerable.Range(0, 5)
					.Select(index => Entry($"data/icons/md04/part-{index}.bin", BackupComponentGroup.Icons) with
					{
						DeclaredSize = BackupArchiveLimits.MaxSingleEntryBytes
					})
			]);
	}

	[TestCaseSource(nameof(UnsafeArchives))]
	public async Task An_unsafe_archive_is_refused_before_anything_is_written(
		Func<BackupRestoreArchiveSafetyTests, CraftedEntry[]> entries,
		string expectedCode)
	{
		var crafted = Craft(entries(this), null);

		var prepare = await PrepareImported(crafted);

		await AssertRefused(prepare, expectedCode);
	}

	[Test]
	public async Task An_unsafe_entry_is_refused_even_when_its_group_is_not_selected()
	{
		var crafted = Craft([Entry(TraversalFrom("data/scripts/", '/'), BackupComponentGroup.Scripts)], null);

		var prepare = await PrepareImported(crafted, ["Icons"]);

		await AssertRefused(prepare, "InvalidArchive");
	}

	[Test]
	[Platform(Exclude = "Win")]
	public async Task A_file_named_with_a_colon_still_restores_where_the_file_system_allows_it()
	{
		const string colonFile = "data/icons/md04/taken-12:30.bin";

		var prepare = await PrepareImported(Craft([Entry(colonFile, BackupComponentGroup.Icons)], null));

		try
		{
			Assert.That(prepare.GetProperty("success").GetBoolean(), Is.True, prepare.ToString());
			var apply = Path.Combine(_paths.RestoreStagingDirectory,
				prepare.GetProperty("restoreId").GetGuid().ToString("N"),
				"apply");
			Assert.That(File.Exists(Path.Combine(apply, Native(colonFile))), Is.True);
		}
		finally
		{
			await CancelIfStaged(prepare);
		}
	}

	[TestCase("oversized")]
	[TestCase("malformed")]
	public async Task Inspecting_an_archive_with_an_unreadable_content_index_still_lists_its_components(string kind)
	{
		var index = kind == "oversized"
			? Encoding.UTF8.GetBytes("{\"entries\":[]"
				+ new string(' ', (int)BackupArchiveLimits.MaxContentIndexBytes)
				+ "}")
			: Encoding.UTF8.GetBytes("{\"entries\": [ not json");
		var path = await WriteCrafted(Craft([], index));

		var inspected = await ReadJson(await Send(HttpMethod.Post, "/api/backups/inspect-path", new { path }));

		Assert.Multiple(() =>
		{
			Assert.That(inspected.GetProperty("success").GetBoolean(), Is.True, inspected.ToString());
			Assert.That(inspected.GetProperty("components")
					.EnumerateArray()
					.Select(c => c.GetProperty("id").GetString()),
				Is.EquivalentTo(_components));
		});
	}

	[Test]
	public async Task A_content_index_beyond_its_size_limit_is_refused()
	{
		var padded = Encoding.UTF8.GetBytes("{\"entries\":[]"
			+ new string(' ', (int)BackupArchiveLimits.MaxContentIndexBytes)
			+ "}");

		var prepare = await PrepareImported(Craft([], padded));

		await AssertRefused(prepare, "TooLarge");
	}

	[Test]
	public async Task A_content_index_listing_more_entries_than_a_backup_can_hold_is_refused()
	{
		var entry = new { path = OwnedFile, size = 0, sha256 = "", component = "Icons" };
		var index = JsonSerializer.SerializeToUtf8Bytes(new
		{
			entries = Enumerable.Repeat(entry, BackupArchiveLimits.MaxInnerEntries + 1)
		});

		var prepare = await PrepareImported(Craft([], index));

		await AssertRefused(prepare, "TooLarge");
	}

	[Test]
	public async Task A_repacked_backup_without_unsafe_entries_still_stages_its_files()
	{
		var crafted = Craft([Entry("data/icons/md04/added.bin", BackupComponentGroup.Icons)], null);

		var prepare = await PrepareImported(crafted);

		try
		{
			Assert.That(prepare.GetProperty("success").GetBoolean(), Is.True, prepare.ToString());
			var apply = Path.Combine(_paths.RestoreStagingDirectory,
				prepare.GetProperty("restoreId").GetGuid().ToString("N"),
				"apply");
			Assert.Multiple(() =>
			{
				Assert.That(File.ReadAllBytes(Path.Combine(apply, Native(OwnedFile))), Is.EqualTo(_originalContent));
				Assert.That(File.Exists(Path.Combine(apply, Native("data/icons/md04/added.bin"))), Is.True);
			});
		}
		finally
		{
			await CancelIfStaged(prepare);
		}
	}

	[Test]
	public async Task A_backup_whose_database_a_newer_host_migrated_is_refused_before_anything_is_replaced()
	{
		var crafted = Craft([],
			null,
			database => LegacyEvolveDatabase.Execute(database,
				$"INSERT INTO \"{DatabaseMigrationHelper.HistoryTable}\" (\"MigrationId\", \"ProductVersion\") "
				+ "VALUES ('99991231235959_FromANewerMacroDeck', '99.0.0');"));

		var prepare = await PrepareImported(crafted);

		await AssertRefused(prepare, nameof(BackupError.UnsupportedVersion));
	}

	[Test]
	public async Task A_backup_whose_database_cannot_be_migrated_is_refused_as_incompatible()
	{
		var crafted = Craft([],
			null,
			database => LegacyEvolveDatabase.Execute(database,
				$"DELETE FROM \"{DatabaseMigrationHelper.HistoryTable}\";"));

		var prepare = await PrepareImported(crafted);

		await AssertRefused(prepare, nameof(BackupError.IncompatibleVersion));
	}

	private async Task AssertRefused(JsonElement prepare, string expectedCode)
	{
		try
		{
			var listed = await ReadJson(await Send(HttpMethod.Get, "/api/backups"));
			var error = prepare.GetProperty("error");
			var refused = error.ValueKind == JsonValueKind.Object;

			Assert.Multiple(() =>
			{
				Assert.That(prepare.GetProperty("success").GetBoolean(), Is.False, prepare.ToString());
				Assert.That(refused ? error.GetProperty("code").GetString() : null, Is.EqualTo(expectedCode));
				Assert.That(refused ? LocalizedKey(error.GetProperty("message")) : null,
					Is.EqualTo($"Errors.Backup.{expectedCode}"),
					"the user sees the localized message, not archive content");
				Assert.That(Directory.Exists(_escapeDirectory),
					Is.False,
					"nothing may be written outside the data folder");
				Assert.That(Directory.EnumerateDirectories(_paths.RestoreStagingDirectory), Is.Empty);
				Assert.That(listed.TryGetProperty("pendingRestore", out var pending)
					&& pending.ValueKind != JsonValueKind.Null,
					Is.False);
				Assert.That(File.ReadAllBytes(DataPath(OwnedFile)), Is.EqualTo(_originalContent));
			});
		}
		finally
		{
			await CancelIfStaged(prepare);
		}
	}

	private static string? LocalizedKey(JsonElement message)
	{
		return message.ValueKind == JsonValueKind.Object
			&& message.TryGetProperty(LocalizedTextJsonConverter.Marker, out var localized)
				? localized.GetProperty("key").GetString()
				: null;
	}

	private async Task CancelIfStaged(JsonElement prepare)
	{
		if (prepare.TryGetProperty("restoreId", out var restoreId) && restoreId.ValueKind == JsonValueKind.String)
		{
			await Send(HttpMethod.Post, "/api/backups/restore/cancel", new { restoreId = restoreId.GetGuid() });
		}
	}

	private async Task<JsonElement> PrepareImported(byte[] archive, IReadOnlyList<string>? components = null)
	{
		var path = await WriteCrafted(archive);

		var imported = await ReadJson(await Send(HttpMethod.Post, "/api/backups/import-path", new { path }));
		Assert.That(imported.GetProperty("success").GetBoolean(), Is.True, imported.ToString());
		var backupId = imported.GetProperty("backup").GetProperty("id").GetString();

		return await ReadJson(await Send(HttpMethod.Post,
			"/api/backups/restore/prepare",
			new { backupId, components = components ?? _components }));
	}

	private async Task<string> WriteCrafted(byte[] archive)
	{
		var path = Path.Combine(_testRoot, $"crafted-{Guid.NewGuid():N}{BackupFileNames.Extension}");
		await File.WriteAllBytesAsync(path, archive);

		return path;
	}

	private byte[] Craft(CraftedEntry[] additions, byte[]? indexOverride, Action<string>? alterDatabase = null)
	{
		var key = RecoveryKey();
		using var source = new MemoryStream(_sourceArchive);
		var manifest = new BackupArchiveReader().ReadManifest(source)!;
		manifest.BackupId = Guid.NewGuid();
		var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, PersistenceJsonOptions.Default);

		var payloadPath = Path.Combine(_testRoot, $"payload-{Guid.NewGuid():N}.zip");
		source.Position = 0;
		new BackupArchiveReader().DecryptPayload(source, key, payloadPath).GetAwaiter().GetResult();

		using var payload = new MemoryStream();
		using (var original = ZipFile.OpenRead(payloadPath))
		using (var repacked = new ZipArchive(payload, ZipArchiveMode.Create, true))
		{
			BackupContentIndex? index = null;
			byte[]? alteredDatabase = null;
			foreach (var entry in original.Entries)
			{
				using var stream = entry.Open();
				if (entry.FullName == BackupFileNames.ContentIndexEntry)
				{
					index = JsonSerializer.Deserialize<BackupContentIndex>(stream, PersistenceJsonOptions.Default);
					continue;
				}

				if (entry.FullName == BackupFileNames.DatabaseEntry && alterDatabase is not null)
				{
					alteredDatabase = AlteredDatabase(stream, alterDatabase);
					using var altered = repacked.CreateEntry(entry.FullName).Open();
					altered.Write(alteredDatabase);
					continue;
				}

				using var copy = repacked.CreateEntry(entry.FullName).Open();
				stream.CopyTo(copy);
			}

			foreach (var addition in additions)
			{
				var added = repacked.CreateEntry(BackupFileNames.FilesPrefix + addition.Path);
				using (var stream = added.Open())
				{
					stream.Write(addition.Content);
				}

				index!.Entries.Add(new BackupContentEntry
				{
					Path = addition.Path,
					Size = addition.DeclaredSize ?? addition.Content.Length,
					Sha256 = Convert.ToHexStringLower(SHA256.HashData(addition.Content)),
					Component = addition.Component
				});
			}

			if (alteredDatabase is not null)
			{
				index!.Database!.Size = alteredDatabase.Length;
				index.Database.Sha256 = Convert.ToHexStringLower(SHA256.HashData(alteredDatabase));
			}

			var indexEntry = repacked.CreateEntry(BackupFileNames.ContentIndexEntry, CompressionLevel.Optimal);
			using var indexStream = indexEntry.Open();
			indexStream.Write(indexOverride
				?? JsonSerializer.SerializeToUtf8Bytes(index, PersistenceJsonOptions.Default));
		}

		File.Delete(payloadPath);

		using var output = new MemoryStream();
		using (var outer = new ZipArchive(output, ZipArchiveMode.Create, true))
		{
			using (var manifestStream = outer.CreateEntry(BackupFileNames.ManifestEntry).Open())
			{
				manifestStream.Write(manifestBytes);
			}

			using var payloadStream = outer.CreateEntry(BackupFileNames.PayloadEntry, CompressionLevel.NoCompression)
				.Open();
			using var encryptor = BackupPayloadCrypto.CreateEncryptor(payloadStream, key, manifestBytes, true);
			payload.Position = 0;
			payload.CopyTo(encryptor);
			encryptor.Finish();
		}

		return output.ToArray();
	}

	private byte[] AlteredDatabase(Stream source, Action<string> alter)
	{
		var path = Path.Combine(_testRoot, $"database-{Guid.NewGuid():N}.db");
		using (var file = File.Create(path))
		{
			source.CopyTo(file);
		}

		alter(path);
		SqliteConnection.ClearAllPools();
		var bytes = File.ReadAllBytes(path);
		File.Delete(path);

		return bytes;
	}

	private byte[] RecoveryKey()
	{
		using var scope = _host.Services.CreateScope();
		var resolved = scope.ServiceProvider.GetRequiredService<IBackupRecoveryKeyService>()
			.Resolve()
			.GetAwaiter()
			.GetResult();

		return resolved.Data!;
	}

	private string EscapeTarget => Path.Combine(_escapeDirectory, EscapeFileName);

	private string TraversalFrom(string ownedFolder, char separator)
	{
		var stagingDepth = Path.GetRelativePath(_paths.DataRootDirectory, _paths.RestoreStagingDirectory)
			.Split(Path.DirectorySeparatorChar)
			.Length;
		var folderDepth = ownedFolder.TrimEnd(separator).Split(separator).Length;
		var up = string.Concat(Enumerable.Repeat(".." + separator, folderDepth + stagingDepth + 3));

		return ownedFolder + up + Path.GetFileName(_escapeDirectory) + separator + EscapeFileName;
	}

	private static CraftedEntry Entry(string path, BackupComponentGroup component)
	{
		return new CraftedEntry(path, component, Encoding.UTF8.GetBytes("payload"), null);
	}

	private static TestCaseData Case(
		string name,
		string expectedCode,
		Func<BackupRestoreArchiveSafetyTests, CraftedEntry[]> entries)
	{
		return new TestCaseData(entries, expectedCode).SetArgDisplayNames(name);
	}

	private string DataPath(string relative)
	{
		return Path.Combine(_paths.DataRootDirectory, Native(relative));
	}

	private static string Native(string relative)
	{
		return relative.Replace('/', Path.DirectorySeparatorChar);
	}

	private Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body = null)
	{
		var request = new HttpRequestMessage(method, path);
		if (body is not null)
		{
			request.Content = JsonContent.Create(body);
		}

		request.Headers.Add(LoopbackHeader, "1");

		return _client.SendAsync(request);
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

	public sealed record CraftedEntry(
		string Path,
		BackupComponentGroup Component,
		byte[] Content,
		long? DeclaredSize);

	private sealed class LoopbackStartupFilter : IStartupFilter
	{
		public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
		{
			return app =>
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
}
