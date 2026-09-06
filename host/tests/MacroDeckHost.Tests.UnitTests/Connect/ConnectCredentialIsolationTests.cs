using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Connect;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Backups;
using MacroDeckHost.Infrastructure.Backups.Restore;
using MacroDeckHost.Infrastructure.Connect;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Infrastructure.Persistence.Repositories;
using MacroDeckHost.Infrastructure.Secrets;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Connect;

[TestFixture]
[NonParallelizable]
public class ConnectCredentialIsolationTests
{
	private string _dataDir = null!;
	private string? _previousDataDir;
	private MacroDeckPaths _paths = null!;
	private ServiceProvider _provider = null!;
	private SecretServiceConnectCredentialStore _store = null!;

	[SetUp]
	public void SetUp()
	{
		_dataDir = Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"));
		_previousDataDir = Environment.GetEnvironmentVariable("MACRODECK_DATA_DIR");
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _dataDir);

		_paths = new MacroDeckPaths();
		_paths.EnsureDirectoriesExist();
		DatabaseMigrationHelper.MigrateDatabase(_paths);

		var services = new ServiceCollection();
		services.AddSingleton<ILogger>(Log.Logger);
		services.AddSingleton<IMacroDeckPaths>(_paths);
		services.AddDbContext<DatabaseContext>();
		services.AddScoped<ISecretRepository, SecretRepository>();
		services.AddScoped<ISecretService, SecretService>();
		services.AddScoped<IAppPreferenceRepository, AppPreferenceRepository>();
		services.AddDataProtection()
			.PersistKeysToFileSystem(new DirectoryInfo(_paths.KeysDirectory))
			.SetApplicationName("MacroDeck");

		_provider = services.BuildServiceProvider();
		_store = new SecretServiceConnectCredentialStore(_provider.GetRequiredService<IServiceScopeFactory>(),
			Log.Logger);
	}

	[TearDown]
	public void TearDown()
	{
		_provider.Dispose();
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _previousDataDir);

		if (Directory.Exists(_dataDir))
		{
			Directory.Delete(_dataDir, recursive: true);
		}
	}

	[Test]
	public async Task A_saved_refresh_token_appears_nowhere_in_the_data_directory()
	{
		var marker = Marker();
		await _store.Save(new ConnectCredential(marker, "sub-1", "Ada", null, DateTimeOffset.UnixEpoch));

		Assert.That(FilesContaining(marker), Is.Empty, "the refresh token was written in the clear");
		Assert.That((await _store.Load())!.RefreshToken, Is.EqualTo(marker));

		await _store.Clear();

		Assert.Multiple(async () =>
		{
			Assert.That(await _store.Load(), Is.Null);
			Assert.That(FilesContaining(marker), Is.Empty);
		});
	}

	[Test]
	public async Task A_backup_archive_carries_no_connect_credential()
	{
		var marker = Marker();
		await _store.Save(new ConnectCredential(marker, "sub-1", "Ada", null, DateTimeOffset.UnixEpoch));

		var recoveryKey = RandomNumberGenerator.GetBytes(32);
		var source = new BackupSnapshotSource(_paths);
		var snapshot = Path.Combine(_dataDir, "snapshot.db");
		await source.CopyDatabase(snapshot);

		using var destination = new MemoryStream();
		await new BackupArchiveWriter().Write(destination,
			new BackupArchiveWriteRequest(Manifest(), source.Plan(), snapshot),
			recoveryKey);

		using var archive = new MemoryStream(destination.ToArray());
		var payload = Path.Combine(_dataDir, "payload.zip");
		await new BackupArchiveReader().DecryptPayload(archive, recoveryKey, payload);

		var hits = new List<string>();
		using (var inner = ZipFile.OpenRead(payload))
		{
			foreach (var entry in inner.Entries)
			{
				using var stream = entry.Open();
				using var buffer = new MemoryStream();
				await stream.CopyToAsync(buffer);

				if (ContainsMarker(buffer.ToArray(), marker))
				{
					hits.Add(entry.FullName);
				}
			}
		}

		Assert.That(hits, Is.Empty, "the archive carried the Connect credential");
	}

	[Test]
	public async Task Restoring_an_archive_from_another_machine_does_not_resurrect_a_session()
	{
		// The archive is built on another machine that was signed in; this installation is not.
		await _store.Save(new ConnectCredential(Marker(), "sub-elsewhere", "Ada", null, DateTimeOffset.UnixEpoch));

		var foreignDatabase = Path.Combine(_dataDir, "foreign.db");
		File.Copy(_paths.DatabasePath, foreignDatabase);

		await _store.Clear();

		var applyDirectory = Path.Combine(_paths.RestoreStagingDirectory, "apply");
		Directory.CreateDirectory(applyDirectory);

		var document = new PendingRestoreDocument
		{
			RestoreId = Guid.NewGuid(),
			BackupId = Guid.NewGuid(),
			StagedAt = DateTimeOffset.UnixEpoch,
			Components = [.. BackupComponentGroups.AllIds],
			StagingDirectory = _paths.RestoreStagingDirectory,
			ApplyDirectory = applyDirectory,
			DatabasePath = foreignDatabase,
			Tables = ["secret", "app_preference"]
		};

		await File.WriteAllBytesAsync(Path.Combine(_paths.RestoreStagingDirectory, PendingRestoreDocument.FileName),
			JsonSerializer.SerializeToUtf8Bytes(document, PersistenceJsonOptions.Default));

		Assert.That(StagedRestoreApplier.ApplyPending(_paths), Is.EqualTo(StagedRestoreOutcome.Applied));

		// The foreign archive really did carry a Connect secret row; what must not survive is the pointer
		// that would turn it back into a session.
		Assert.That(ConnectSecretRows(), Is.GreaterThan(0));
		Assert.That(await _store.Load(), Is.Null);
	}

	private static bool ContainsMarker(byte[] content, string marker)
		=> Contains(content, Encoding.UTF8.GetBytes(marker)) ||
			Contains(content, Encoding.Unicode.GetBytes(marker)) ||
			Contains(content, Encoding.UTF8.GetBytes(Convert.ToBase64String(Encoding.UTF8.GetBytes(marker))));

	private static bool Contains(byte[] haystack, byte[] needle)
		=> haystack.AsSpan().IndexOf(needle) >= 0;

	private static string Marker()
		=> Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

	private static BackupArchiveManifest Manifest()
		=> new()
		{
			BackupId = Guid.NewGuid(),
			MacroDeckVersion = "3.0.0",
			CreatedAt = DateTimeOffset.UnixEpoch,
			Trigger = BackupTrigger.Manual,
			RecoveryKeyId = "test",
			HostPlatform = "test",
			Components = [.. BackupComponentGroups.AllIds]
		};

	private List<string> FilesContaining(string marker)
	{
		var hits = new List<string>();

		foreach (var file in Directory.EnumerateFiles(_dataDir, "*", SearchOption.AllDirectories))
		{
			try
			{
				if (ContainsMarker(File.ReadAllBytes(file), marker))
				{
					hits.Add(file);
				}
			}
			catch (IOException)
			{
			}
		}

		return hits;
	}

	private int ConnectSecretRows()
	{
		using var connection = new SqliteConnection(
			new SqliteConnectionStringBuilder { DataSource = _paths.DatabasePath }.ToString());
		connection.Open();

		using var command = connection.CreateCommand();
		command.CommandText = $"SELECT COUNT(*) FROM secret WHERE s_kind = {(int)SecretKind.ConnectCredential};";

		return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
	}
}
