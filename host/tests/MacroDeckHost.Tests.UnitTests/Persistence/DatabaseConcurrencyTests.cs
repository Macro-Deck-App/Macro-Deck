using MacroDeckHost.Application.Paths;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Persistence;

[TestFixture]
[NonParallelizable]
public class DatabaseConcurrencyTests
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
		SqliteConnection.ClearAllPools();

		if (Directory.Exists(_dataDir))
		{
			Directory.Delete(_dataDir, recursive: true);
		}
	}

	[Test]
	public async Task A_write_commits_while_another_context_is_still_reading()
	{
		await using (var seed = new DatabaseContext(Log.Logger, _paths))
		{
			seed.Devices.AddRange(NewDevice("a"), NewDevice("b"));
			await seed.SaveChangesAsync();
		}

		await using var readerContext = new DatabaseContext(Log.Logger, _paths);
		await using var reading = readerContext.Devices.AsAsyncEnumerable().GetAsyncEnumerator();
		Assert.That(await reading.MoveNextAsync(), Is.True);

		await using var writerContext = new DatabaseContext(Log.Logger, _paths);
		writerContext.Devices.Add(NewDevice("c"));
		writerContext.AppPreferences.Add(new AppPreferenceEntity { Key = "concurrency.test", Value = "x" });
		// Microsoft.Data.Sqlite waits out a busy lock synchronously, so the save needs its own thread.
		var save = Task.Run(() => writerContext.SaveChangesAsync());

		var finishedWhileReading = await Task.WhenAny(save, Task.Delay(TimeSpan.FromSeconds(5))) == save;
		await reading.DisposeAsync();
		var saved = await save;

		Assert.Multiple(() =>
		{
			Assert.That(finishedWhileReading, Is.True, "the write waited for the reader to finish");
			Assert.That(saved, Is.EqualTo(2));
		});
	}

	[Test]
	public void The_migrated_database_uses_write_ahead_logging()
	{
		using var connection = new SqliteConnection($"Data Source={_paths.DatabasePath}");
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText = "PRAGMA journal_mode;";

		Assert.That(command.ExecuteScalar(), Is.EqualTo("wal"));
	}

	private static DeviceEntity NewDevice(string name) => new() { SecretHash = string.Empty, Name = name };
}
