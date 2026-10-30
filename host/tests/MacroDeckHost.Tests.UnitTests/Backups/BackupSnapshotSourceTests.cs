using MacroDeckHost.Application.Paths;
using MacroDeckHost.Infrastructure.Backups;
using MacroDeckHost.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace MacroDeckHost.Tests.UnitTests.Backups;

[TestFixture]
[NonParallelizable]
public class BackupSnapshotSourceTests
{
	private string _dataDir = null!;
	private string? _previousDataDir;
	private MacroDeckPaths _paths = null!;

	[SetUp]
	public void SetUp()
	{
		_dataDir = Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"));
		_previousDataDir = Environment.GetEnvironmentVariable("MACRODECK_DATA_DIR");
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _dataDir);

		_paths = new MacroDeckPaths();
		_paths.EnsureDirectoriesExist();
		DatabaseMigrationHelper.MigrateDatabase(_paths);
	}

	[TearDown]
	public void TearDown()
	{
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _previousDataDir);

		if (Directory.Exists(_dataDir))
		{
			Directory.Delete(_dataDir, true);
		}
	}

	[Test]
	public async Task A_snapshot_waits_for_a_writer_that_briefly_locks_the_database()
	{
		var writer = new SqliteConnection(new SqliteConnectionStringBuilder
		{
			DataSource = _paths.DatabasePath,
			Pooling = false
		}.ToString());
		await writer.OpenAsync();
		await using (var command = writer.CreateCommand())
		{
			command.CommandText = """
								  PRAGMA locking_mode = EXCLUSIVE;
								  CREATE TABLE snapshot_probe (value TEXT);
								  INSERT INTO snapshot_probe VALUES ('written while locked');
								  """;
			await command.ExecuteNonQueryAsync();
		}

		var release = Task.Run(async () =>
		{
			await Task.Delay(TimeSpan.FromMilliseconds(500));
			await writer.DisposeAsync();
		});

		var snapshot = Path.Combine(_dataDir, "snapshot.db");
		await new BackupSnapshotSource(_paths).CopyDatabase(snapshot);
		await release;

		await using var copy = new SqliteConnection($"Data Source={snapshot};Pooling=False");
		await copy.OpenAsync();
		await using var read = copy.CreateCommand();
		read.CommandText = "SELECT value FROM snapshot_probe;";
		Assert.That(await read.ExecuteScalarAsync(), Is.EqualTo("written while locked"));
	}
}
