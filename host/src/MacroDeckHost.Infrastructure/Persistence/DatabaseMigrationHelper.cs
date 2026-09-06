using EvolveDb;
using MacroDeckHost.Application.Paths;
using Microsoft.Data.Sqlite;
using Serilog;

namespace MacroDeckHost.Infrastructure.Persistence;

public static class DatabaseMigrationHelper
{
	public const string MetadataTable = "database_migration_history";

	public static void MigrateDatabase(IMacroDeckPaths paths)
	{
		try
		{
			Migrate(paths.DatabasePath, paths.DatabaseMigrationsDirectory);
		}
		catch (Exception ex)
		{
			Log.Fatal(ex, "Database migration failed");
			throw;
		}
	}

	/// <summary>
	/// Migrates a database file that is not the live one. Restore uses this on the copy inside an archive:
	/// the archive carries its own migration history, so Evolve knows exactly which scripts - including
	/// their data backfills - still have to run before the rows can be read into the current schema.
	/// </summary>
	public static void Migrate(string databasePath, string migrationsDirectory)
	{
		var connectionStringBuilder = new SqliteConnectionStringBuilder { DataSource = databasePath };
		var connection = new SqliteConnection(connectionStringBuilder.ToString());
		var evolve = new Evolve(connection, Log.Information)
		{
			Locations = [migrationsDirectory],
			IsEraseDisabled = true,
			MetadataTableName = MetadataTable
		};

		evolve.Migrate();
	}
}
