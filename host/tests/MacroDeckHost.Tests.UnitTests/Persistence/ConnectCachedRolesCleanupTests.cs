using Microsoft.Data.Sqlite;

namespace MacroDeckHost.Tests.UnitTests.Persistence;

public class ConnectCachedRolesCleanupTests
{
	private string _path = null!;

	[SetUp]
	public void SetUp() => _path = Path.Combine(Path.GetTempPath(),
		"macro-deck-tests",
		Guid.NewGuid().ToString("N"),
		"database.db");

	[TearDown]
	public void TearDown()
	{
		SqliteConnection.ClearAllPools();
		var directory = Path.GetDirectoryName(_path)!;
		if (Directory.Exists(directory))
		{
			Directory.Delete(directory, true);
		}
	}

	[Test]
	public void Cached_roles_are_removed_and_the_rest_of_the_credential_is_kept()
	{
		Seed(("connect.credentialCachedRoles", "[\"StoreTester\"]"),
			("connect.credentialSubject", "sub-1"),
			("connect.credentialCachedDisplayName", "Ada"));

		Migrate();

		Assert.That(Keys(), Is.EquivalentTo(new[] { "connect.credentialSubject", "connect.credentialCachedDisplayName" }));
	}

	private void Seed(params (string Key, string Value)[] preferences)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
		using var connection = Open();
		Execute(connection,
			"""
			CREATE TABLE app_preference
			(
			    ap_key        TEXT NOT NULL CONSTRAINT pk_app_preference PRIMARY KEY,
			    ap_value      TEXT NOT NULL DEFAULT '',
			    ap_created_at TEXT NOT NULL,
			    ap_updated_at TEXT NOT NULL
			);
			""");

		foreach (var (key, value) in preferences)
		{
			using var command = connection.CreateCommand();
			command.CommandText = "INSERT INTO app_preference VALUES ($key, $value, '2026-01-01', '2026-01-01');";
			command.Parameters.AddWithValue("$key", key);
			command.Parameters.AddWithValue("$value", value);
			command.ExecuteNonQuery();
		}
	}

	private void Migrate()
	{
		var script = Path.Combine(AppContext.BaseDirectory,
			"DatabaseMigrations",
			"V1_19_0_0__DropConnectCachedRoles.sql");
		Assert.That(File.Exists(script), Is.True, $"migration script not found at {script}");

		using var connection = Open();
		Execute(connection, File.ReadAllText(script));
	}

	private List<string> Keys()
	{
		using var connection = Open();
		using var command = connection.CreateCommand();
		command.CommandText = "SELECT ap_key FROM app_preference;";
		using var reader = command.ExecuteReader();

		var keys = new List<string>();
		while (reader.Read())
		{
			keys.Add(reader.GetString(0));
		}

		return keys;
	}

	private SqliteConnection Open()
	{
		var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path }.ToString());
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
