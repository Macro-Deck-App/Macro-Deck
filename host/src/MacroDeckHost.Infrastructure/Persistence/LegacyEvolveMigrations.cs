using System.Reflection;
using MacroDeckHost.DatabaseMigrations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MacroDeckHost.Infrastructure.Persistence;

internal static class LegacyEvolveMigrations
{
	private const string ResourcePrefix = "MacroDeckHost.LegacyEvolveMigrations.";
	private const string Separator = "__";

	public static IReadOnlyList<LegacyEvolveScript> Scripts { get; } = LoadScripts();

	public static void UpgradeIfNeeded(DatabaseContext context)
	{
		var connection = (SqliteConnection)context.Database.GetDbConnection();

		if (!DatabaseMigrationHelper.TableExists(connection, DatabaseMigrationHelper.LegacyHistoryTable)
			|| DatabaseMigrationHelper.TableExists(connection, DatabaseMigrationHelper.HistoryTable))
		{
			return;
		}

		var pending = PendingScripts(AppliedVersions(connection));
		var historyRepository = context.GetService<IHistoryRepository>();
		var baseline = new HistoryRow(typeof(Initial).GetCustomAttribute<MigrationAttribute>()!.Id,
			ProductInfo.GetVersion());

		using var transaction = connection.BeginTransaction();

		foreach (var script in pending)
		{
			Execute(connection, transaction, script.Sql);
		}

		Execute(connection, transaction, historyRepository.GetCreateIfNotExistsScript());
		Execute(connection, transaction, historyRepository.GetInsertScript(baseline));
		transaction.Commit();
	}

	internal static IReadOnlyList<LegacyEvolveScript> PendingScripts(IReadOnlySet<Version> applied)
	{
		var newest = Scripts[^1].Version;
		var unknown = applied.Where(version => version > newest).ToList();

		if (unknown.Count > 0)
		{
			throw new DatabaseMigrationException(
				$"The database was migrated by a newer Macro Deck (version {unknown.Max()}).")
			{
				FromNewerHost = true
			};
		}

		var lastApplied = applied.Count == 0 ? null : applied.Max();

		if (lastApplied is null)
		{
			return Scripts;
		}

		var skipped = Scripts
			.Where(script => script.Version <= lastApplied && !applied.Contains(script.Version))
			.Select(script => script.Name)
			.ToList();

		if (skipped.Count > 0)
		{
			throw new DatabaseMigrationException(
				$"The database history skips migrations it should contain: {string.Join(", ", skipped)}.");
		}

		return [.. Scripts.Where(script => script.Version > lastApplied)];
	}

	private static HashSet<Version> AppliedVersions(SqliteConnection connection)
	{
		using var command = connection.CreateCommand();
		command.CommandText =
			$"SELECT version FROM {DatabaseMigrationHelper.LegacyHistoryTable} WHERE type = 0 AND success = 1;";

		var versions = new HashSet<Version>();
		using var reader = command.ExecuteReader();

		while (reader.Read())
		{
			versions.Add(Version.Parse(reader.GetString(0)));
		}

		return versions;
	}

	private static List<LegacyEvolveScript> LoadScripts()
	{
		var assembly = typeof(LegacyEvolveMigrations).Assembly;
		var scripts = new List<LegacyEvolveScript>();

		foreach (var resource in assembly.GetManifestResourceNames())
		{
			if (!resource.StartsWith(ResourcePrefix, StringComparison.Ordinal))
			{
				continue;
			}

			var name = resource[ResourcePrefix.Length..];
			var separator = name.IndexOf(Separator, StringComparison.Ordinal);
			var version = Version.Parse(name[1..separator].Replace('_', '.'));

			using var stream = assembly.GetManifestResourceStream(resource)!;
			using var reader = new StreamReader(stream);
			scripts.Add(new LegacyEvolveScript(version, name, reader.ReadToEnd()));
		}

		return [.. scripts.OrderBy(script => script.Version)];
	}

	private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
	{
		using var command = connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandText = sql;
		command.ExecuteNonQuery();
	}
}
