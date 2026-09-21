using Microsoft.Data.Sqlite;

namespace MacroDeckHost.Tests.UnitTests.Persistence;

// The backfill is what keeps reuse detection whole for chains that predate rt_family_id: without it a
// returning predecessor would revoke only itself and leave the live token at its chain's tail alone.
public class RefreshTokenFamilyBackfillTests
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
	public void Every_token_of_an_existing_chain_ends_up_in_one_family()
	{
		Seed(
			("a1", "a2"),
			("a2", "a3"),
			("a3", null),
			("b1", null),
			("c1", "c2"),
			("c2", null));

		Migrate();

		var families = Families();
		Assert.Multiple(() =>
		{
			Assert.That(families["a1"], Is.EqualTo(families["a2"]).And.EqualTo(families["a3"]));
			Assert.That(families["c1"], Is.EqualTo(families["c2"]));
			Assert.That(families["b1"], Is.Not.EqualTo(families["a1"]));
			Assert.That(families["b1"], Is.Not.EqualTo(families["c1"]));
			Assert.That(families.Values, Has.None.Empty);
		});
	}

	[Test]
	public void A_chain_whose_successor_row_is_already_gone_still_gets_a_family()
	{
		Seed(("d1", "d2-deleted"));

		Migrate();

		Assert.That(Families()["d1"], Is.Not.Empty);
	}

	private void Seed(params (string Id, string? ReplacedBy)[] tokens)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
		using var connection = Open();
		Execute(connection,
			"""
			CREATE TABLE refresh_token
			(
			    rt_id                TEXT NOT NULL CONSTRAINT pk_refresh_token PRIMARY KEY,
			    rt_user_id           TEXT NOT NULL,
			    rt_token_hash        TEXT NOT NULL,
			    rt_scope             INTEGER NOT NULL,
			    rt_expires_at        TEXT NOT NULL,
			    rt_revoked_at        TEXT NULL,
			    rt_replaced_by_id    TEXT NULL,
			    rt_created_at        TEXT NOT NULL,
			    rt_rotated_by_grace  INTEGER NOT NULL DEFAULT 0
			);
			""");

		foreach (var (id, replacedBy) in tokens)
		{
			using var command = connection.CreateCommand();
			command.CommandText =
				"""
				INSERT INTO refresh_token
				VALUES ($id, 'user', $hash, 1, '2030-01-01', NULL, $replaced, '2026-01-01', 0);
				""";
			command.Parameters.AddWithValue("$id", id);
			command.Parameters.AddWithValue("$hash", "hash-" + id);
			command.Parameters.AddWithValue("$replaced", (object?)replacedBy ?? DBNull.Value);
			command.ExecuteNonQuery();
		}
	}

	private void Migrate()
	{
		var script = Path.Combine(AppContext.BaseDirectory,
			"DatabaseMigrations",
			"V1_17_0_0__AddRefreshTokenFamily.sql");
		Assert.That(File.Exists(script), Is.True, $"migration script not found at {script}");

		using var connection = Open();
		Execute(connection, File.ReadAllText(script));
	}

	private Dictionary<string, string> Families()
	{
		using var connection = Open();
		using var command = connection.CreateCommand();
		command.CommandText = "SELECT rt_id, rt_family_id FROM refresh_token;";
		using var reader = command.ExecuteReader();

		var families = new Dictionary<string, string>(StringComparer.Ordinal);
		while (reader.Read())
		{
			families[reader.GetString(0)] = reader.GetString(1);
		}

		return families;
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
