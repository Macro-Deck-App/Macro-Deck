using System.Globalization;
using MacroDeckHost.Application.Paths;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace MacroDeckHost.Infrastructure.Persistence;

public static class DatabaseMigrationHelper
{
	public const string HistoryTable = "__EFMigrationsHistory";
	public const string LegacyHistoryTable = "database_migration_history";
	public const string LockTable = "__EFMigrationsLock";

	public static void MigrateDatabase(IMacroDeckPaths paths)
	{
		try
		{
			Migrate(paths.DatabasePath);
			UseWriteAheadLog(paths.DatabasePath);
		}
		catch (Exception ex)
		{
			Log.Fatal(ex, "Database migration failed");
			throw;
		}
	}

	// WAL lets a commit proceed while other connections read; the mode persists in the file.
	private static void UseWriteAheadLog(string databasePath)
	{
		using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
			{ DataSource = databasePath }.ToString());
		connection.Open();

		using var command = connection.CreateCommand();
		command.CommandText = "PRAGMA journal_mode=WAL;";
		var mode = command.ExecuteScalar() as string;

		if (!string.Equals(mode, "wal", StringComparison.OrdinalIgnoreCase))
		{
			Log.Warning("The database could not switch to WAL and stays in journal mode {JournalMode}", mode);
		}
	}

	public static void Migrate(string databasePath)
	{
		try
		{
			using var context = new DatabaseContext(Log.Logger, databasePath);
			context.Database.OpenConnection();

			try
			{
				RefuseNewerDatabase(context);
				LegacyEvolveMigrations.UpgradeIfNeeded(context);
				ReleaseStaleMigrationLock((SqliteConnection)context.Database.GetDbConnection());
			}
			finally
			{
				context.Database.CloseConnection();
			}

			context.Database.Migrate();
		}
		catch (Exception ex) when (ex is not DatabaseMigrationException)
		{
			throw new DatabaseMigrationException($"The database {databasePath} could not be migrated.", ex);
		}
	}

	// EF Core skips applied migrations it does not know, so an older host would run on a newer schema.
	private static void RefuseNewerDatabase(DatabaseContext context)
	{
		var unknown = context.Database.GetAppliedMigrations()
			.Except(context.Database.GetMigrations(), StringComparer.Ordinal)
			.Order(StringComparer.Ordinal)
			.ToList();

		if (unknown.Count > 0)
		{
			throw new DatabaseMigrationException(
				$"The database was migrated by a newer Macro Deck (migrations {string.Join(", ", unknown)}).")
			{
				FromNewerHost = true
			};
		}
	}

	// EF Core's SQLite migration lock never expires, so a row left by a killed host would block every later
	// start. Two hosts racing past the best-effort single-instance probe can fail a start, but never lose data.
	private static void ReleaseStaleMigrationLock(SqliteConnection connection)
	{
		if (!TableExists(connection, LockTable))
		{
			return;
		}

		using var command = connection.CreateCommand();
		command.CommandText = $"DELETE FROM \"{LockTable}\";";
		command.ExecuteNonQuery();
	}

	internal static bool TableExists(SqliteConnection connection, string table)
	{
		using var command = connection.CreateCommand();
		command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
		command.Parameters.AddWithValue("$name", table);

		return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
	}
}
