using System.Text.Json;
using System.Text.Json.Serialization;
using MacroDeckHost.Application.Backups;
using MacroDeckHost.Infrastructure.Backups.Restore;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Data.Sqlite;

namespace MacroDeckHost.Tests.UnitTests.Backups;

[TestFixture]
public class LegacyArchiveRestoreTests
{
	private static readonly JsonSerializerOptions _json =
		new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

	private TestPaths _paths = null!;
	private string _archiveDatabase = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		_paths.EnsureDirectoriesExist();
		_archiveDatabase = Path.Combine(_paths.RestoreStagingDirectory, "archive.db");
		DatabaseMigrationHelper.MigrateDatabase(_paths);
	}

	[TearDown]
	public void TearDown()
	{
		SqliteConnection.ClearAllPools();
		_paths.Cleanup();
	}

	[Test]
	public void An_archive_taken_before_the_switch_restores_its_rows_and_leaves_the_history_alone()
	{
		LegacyEvolveDatabase.Create(_archiveDatabase, new Version(1, 9, 0, 0));
		LegacyEvolveDatabase.Execute(_archiveDatabase,
			"INSERT INTO app_user (u_id, u_username, u_password_hash, u_created_at, u_updated_at) "
			+ "VALUES ('AAAAAAAA-0000-0000-0000-000000000001', 'restored', 'hash', '2026-01-01', '2026-01-01');");
		var historyBefore = History(_paths.DatabasePath);

		DatabaseMigrationHelper.Migrate(_archiveDatabase);
		LegacyEvolveDatabase.Execute(_archiveDatabase,
			$"UPDATE \"{DatabaseMigrationHelper.HistoryTable}\" SET \"ProductVersion\" = 'from-archive';");
		LegacyEvolveDatabase.Execute(_archiveDatabase,
			$"INSERT INTO \"{DatabaseMigrationHelper.LockTable}\" (\"Id\", \"Timestamp\") VALUES (1, '2026-01-01');");
		Apply([
			"app_user",
			DatabaseMigrationHelper.HistoryTable,
			DatabaseMigrationHelper.LegacyHistoryTable,
			DatabaseMigrationHelper.LockTable
		]);

		Assert.Multiple(() =>
		{
			Assert.That(LegacyEvolveDatabase.Query(_paths.DatabasePath, "SELECT u_username FROM app_user;"),
				Is.EqualTo(["restored"]));
			Assert.That(History(_paths.DatabasePath), Is.EqualTo(historyBefore));
			Assert.That(LegacyEvolveDatabase.Tables(_paths.DatabasePath),
				Does.Not.Contain(DatabaseMigrationHelper.LegacyHistoryTable));
			Assert.That(LegacyEvolveDatabase.Query(_paths.DatabasePath,
					$"SELECT * FROM \"{DatabaseMigrationHelper.LockTable}\";"),
				Is.Empty);
		});
	}

	private static List<string> History(string path)
	{
		return LegacyEvolveDatabase.Query(path,
			$"SELECT * FROM \"{DatabaseMigrationHelper.HistoryTable}\" ORDER BY \"MigrationId\";");
	}

	private void Apply(List<string> tables)
	{
		var staging = Path.Combine(_paths.RestoreStagingDirectory, Guid.NewGuid().ToString("N"));
		var apply = Path.Combine(staging, "apply");
		Directory.CreateDirectory(apply);

		var document = new PendingRestoreDocument
		{
			RestoreId = Guid.NewGuid(),
			BackupId = Guid.NewGuid(),
			StagedAt = DateTimeOffset.UnixEpoch,
			Components = [],
			StagingDirectory = staging,
			ApplyDirectory = apply,
			DatabasePath = _archiveDatabase,
			Tables = tables
		};

		File.WriteAllText(Path.Combine(_paths.RestoreStagingDirectory, PendingRestoreDocument.FileName),
			JsonSerializer.Serialize(document, _json));

		Assert.That(StagedRestoreApplier.ApplyPending(_paths), Is.EqualTo(StagedRestoreOutcome.Applied));
	}
}
