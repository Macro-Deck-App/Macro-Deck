using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MacroDeckHost.Application.Backups;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Backups;
using MacroDeckHost.Infrastructure.Backups.Restore;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Data.Sqlite;

namespace MacroDeckHost.Tests.UnitTests.Windows.Backups;

// Windows is the only platform that enforces share modes, so it is the only one where a handle the
// backup forgot to release is observable at all. These assertions are vacuous anywhere else.
[TestFixture]
[Platform("Win")]
public class BackupStagingLockTests
{
	private static readonly JsonSerializerOptions _json =
		new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

	private TestPaths _paths = null!;
	private string _staging = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		_paths.EnsureDirectoriesExist();
		_staging = Path.Combine(_paths.BackupsDirectory, "staging", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_staging);

		CreateDatabase(_paths.DatabasePath);
	}

	[TearDown]
	public void TearDown() => _paths.Cleanup();

	[Test]
	public async Task LeavesTheStagedDatabaseReadableAndRemovableAfterTheSnapshot()
	{
		var source = new BackupSnapshotSource(_paths);
		var copy = Path.Combine(_staging, "database.db");

		await source.CopyDatabase(copy);
		source.ReadSchemaVersion(copy);

		Assert.DoesNotThrow(() => File.OpenRead(copy).Dispose(),
			"the archive writer opens the staged copy exactly this way");
		Assert.DoesNotThrow(() => Directory.Delete(_staging, recursive: true));
	}

	[Test]
	public async Task ReportsWhichFileIsLockedWhenAnotherProcessHoldsTheStagedDatabase()
	{
		var source = new BackupSnapshotSource(_paths);
		var copy = Path.Combine(_staging, "database.db");
		await source.CopyDatabase(copy);

		using var intruder = new FileStream(copy, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

		var manifest = new BackupArchiveManifest
		{
			BackupId = Guid.NewGuid(),
			MacroDeckVersion = "3.0.0",
			CreatedAt = DateTimeOffset.UnixEpoch,
			Trigger = BackupTrigger.Manual,
			RecoveryKeyId = "test",
			HostPlatform = "test",
			Components = [.. BackupComponentGroups.AllIds]
		};

		using var destination = new MemoryStream();
		var locked = Assert.ThrowsAsync<BackupFileLockedException>(async () => await new BackupArchiveWriter().Write(
			destination,
			new BackupArchiveWriteRequest(manifest, source.Plan(), copy),
			RandomNumberGenerator.GetBytes(32)));

		Assert.That(locked!.Path, Is.EqualTo(copy));
		Assert.That(locked.Message, Does.Contain(copy));
	}

	[Test]
	public void RemovesTheRestoreStagingDirectoryAfterSwappingTables()
	{
		var staging = Path.Combine(_paths.RestoreStagingDirectory, Guid.NewGuid().ToString("N"));
		var apply = Path.Combine(staging, "apply");
		Directory.CreateDirectory(apply);

		var restored = Path.Combine(staging, "database.db");
		CreateDatabase(restored);
		Insert(restored, "restored");

		var profile = Path.Combine(apply, "data", "profiles", "living-room.json");
		Directory.CreateDirectory(Path.GetDirectoryName(profile)!);
		File.WriteAllText(profile, """{"name":"Living Room"}""");

		var document = new PendingRestoreDocument
		{
			RestoreId = Guid.NewGuid(),
			BackupId = Guid.NewGuid(),
			StagedAt = DateTimeOffset.UnixEpoch,
			Components = [BackupComponentGroup.Profiles],
			StagingDirectory = staging,
			ApplyDirectory = apply,
			DatabasePath = restored,
			Tables = ["app_preference"],
			Files =
			[
				new PendingRestoreFile
				{
					RelativePath = "data/profiles/living-room.json",
					Sha256 = Sha256(profile),
					Component = BackupComponentGroup.Profiles
				}
			]
		};

		File.WriteAllText(Path.Combine(_paths.RestoreStagingDirectory, PendingRestoreDocument.FileName),
			JsonSerializer.Serialize(document, _json));

		var outcome = StagedRestoreApplier.ApplyPending(_paths);

		Assert.That(outcome, Is.EqualTo(StagedRestoreOutcome.Applied));
		Assert.That(Directory.Exists(staging),
			Is.False,
			"the staged copy of the database must not survive the restore");

		// The applier runs before anything else opens the installation, so it must hand the database
		// over without a handle of its own still on it.
		Assert.DoesNotThrow(() =>
			File.Open(_paths.DatabasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None).Dispose());
	}

	private static void CreateDatabase(string path)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);

		using var connection = new SqliteConnection(
			new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
		connection.Open();

		using var command = connection.CreateCommand();
		command.CommandText = "CREATE TABLE app_preference (ap_key TEXT PRIMARY KEY, ap_value TEXT);";
		command.ExecuteNonQuery();
	}

	private static void Insert(string path, string value)
	{
		using var connection = new SqliteConnection(
			new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
		connection.Open();

		using var command = connection.CreateCommand();
		command.CommandText = "INSERT INTO app_preference (ap_key, ap_value) VALUES ('theme', $value);";
		command.Parameters.AddWithValue("$value", value);
		command.ExecuteNonQuery();
	}

	private static string Sha256(string path)
	{
		using var stream = File.OpenRead(path);

		return Convert.ToHexStringLower(SHA256.HashData(stream));
	}
}
