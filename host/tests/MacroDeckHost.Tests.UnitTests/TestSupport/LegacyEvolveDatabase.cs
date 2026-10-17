using System.Globalization;
using MacroDeckHost.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal static class LegacyEvolveDatabase
{
	private const string HistoryDdl =
		"CREATE TABLE [database_migration_history] ( id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL, "
		+ "type INTEGER, version VARCHAR(50), description VARCHAR(200) NOT NULL, name VARCHAR(300) NOT NULL, "
		+ "checksum VARCHAR(32), installed_by VARCHAR(100) NOT NULL, installed_on TEXT NOT NULL DEFAULT "
		+ "(strftime('%Y-%m-%d %H:%M:%f','now')), success BOOLEAN NOT NULL )";

	private static readonly string[] _historyTables =
	[
		DatabaseMigrationHelper.HistoryTable,
		DatabaseMigrationHelper.LegacyHistoryTable,
		DatabaseMigrationHelper.LockTable
	];

	public static Version Newest => LegacyEvolveMigrations.Scripts[^1].Version;

	public static void Create(string path, Version? appliedUpTo)
	{
		using var connection = Open(path);
		Execute(connection, HistoryDdl);
		Record(connection, 2, "0", "Empty schema found: main.", "main", true);

		if (appliedUpTo is null)
		{
			return;
		}

		foreach (var script in LegacyEvolveMigrations.Scripts.Where(script => script.Version <= appliedUpTo))
		{
			Execute(connection, script.Sql);
			Record(connection, 0, script.Version.ToString(), script.Name, script.Name, true);
		}
	}

	public static void Record(string path, Version version, bool success)
	{
		using var connection = Open(path);
		Record(connection, 0, version.ToString(), "Recorded by a test", $"V{version}.sql", success);
	}

	public static void Execute(string path, string sql)
	{
		using var connection = Open(path);
		Execute(connection, sql);
	}

	public static List<string> Query(string path, string sql)
	{
		using var connection = Open(path);
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		using var reader = command.ExecuteReader();

		var rows = new List<string>();
		while (reader.Read())
		{
			var values = Enumerable.Range(0, reader.FieldCount)
				.Select(index => reader.IsDBNull(index)
					? "null"
					: Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture));
			rows.Add(string.Join("|", values));
		}

		return rows;
	}

	public static List<string> Tables(string path)
	{
		return Query(path, "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;");
	}

	public static List<string> DescribeSchema(string path)
	{
		using var connection = Open(path);
		var description = new List<string>();

		foreach (var table in Query(path,
				"SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';")
			.Except(_historyTables))
		{
			foreach (var column in Query(path,
				$"SELECT name, type, \"notnull\", pk, dflt_value FROM pragma_table_xinfo('{table}');"))
			{
				var parts = column.Split('|');
				raw.sqlite3_table_column_metadata(connection.Handle,
					"main",
					table,
					parts[0],
					out _,
					out var collation,
					out _,
					out _,
					out _);
				description.Add($"{table}.{parts[0]} type={parts[1]} notnull={parts[2]} pk={parts[3]} "
					+ $"default={EvaluateDefault(path, parts[4])} collation={collation}");
			}

			foreach (var index in Query(path,
				$"SELECT name, \"unique\", origin, partial FROM pragma_index_list('{table}');"))
			{
				var name = index.Split('|')[0];
				var columns = Query(path,
					$"SELECT name, coll, \"desc\" FROM pragma_index_xinfo('{name}') WHERE key = 1 ORDER BY seqno;");
				description.Add($"{table} index {index} columns={string.Join(",", columns)}");
			}
		}

		description.Sort(StringComparer.Ordinal);
		return description;
	}

	private static string EvaluateDefault(string path, string expression)
	{
		return expression == "null"
			? "none"
			: Query(path, $"SELECT typeof({expression}) || ':' || quote({expression});").Single();
	}

	private static void Record(
		SqliteConnection connection,
		int type,
		string version,
		string description,
		string name,
		bool success)
	{
		using var command = connection.CreateCommand();
		command.CommandText =
			"INSERT INTO database_migration_history (type, version, description, name, checksum, installed_by, "
			+ "success) VALUES ($type, $version, $description, $name, '', 'test', $success);";
		command.Parameters.AddWithValue("$type", type);
		command.Parameters.AddWithValue("$version", version);
		command.Parameters.AddWithValue("$description", description);
		command.Parameters.AddWithValue("$name", name);
		command.Parameters.AddWithValue("$success", success);
		command.ExecuteNonQuery();
	}

	private static SqliteConnection Open(string path)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		var connection = new SqliteConnection(new SqliteConnectionStringBuilder
			{ DataSource = path, Pooling = false }.ToString());
		connection.Open();
		return connection;
	}

	private static void Execute(SqliteConnection connection, string sql)
	{
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		command.ExecuteNonQuery();
	}
}
