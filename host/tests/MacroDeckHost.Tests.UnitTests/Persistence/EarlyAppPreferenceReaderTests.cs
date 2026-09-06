using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace MacroDeckHost.Tests.UnitTests.Persistence;

public class EarlyAppPreferenceReaderTests
{
	private string _directory = string.Empty;

	[SetUp]
	public void CreateTemporaryDirectory()
	{
		_directory = Path.Combine(Path.GetTempPath(), $"macro-deck-early-preference-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_directory);
	}

	[TearDown]
	public void RemoveTemporaryDirectory()
	{
		SqliteConnection.ClearAllPools();
		if (Directory.Exists(_directory))
		{
			Directory.Delete(_directory, true);
		}
	}

	private string CreateDatabase(params (string Key, string Value)[] preferences)
	{
		var path = Path.Combine(_directory, "database.db");
		using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }
			.ToString());
		connection.Open();

		using (var create = connection.CreateCommand())
		{
			create.CommandText = """
								 CREATE TABLE app_preference
								 (
								     ap_key        TEXT NOT NULL CONSTRAINT pk_app_preference PRIMARY KEY,
								     ap_value      TEXT NOT NULL DEFAULT '',
								     ap_created_at TEXT NOT NULL,
								     ap_updated_at TEXT NOT NULL
								 );
								 """;
			create.ExecuteNonQuery();
		}

		foreach (var (key, value) in preferences)
		{
			using var insert = connection.CreateCommand();
			insert.CommandText =
				"INSERT INTO app_preference (ap_key, ap_value, ap_created_at, ap_updated_at) " +
				"VALUES ($key, $value, '', '')";
			insert.Parameters.AddWithValue("$key", key);
			insert.Parameters.AddWithValue("$value", value);
			insert.ExecuteNonQuery();
		}

		return path;
	}

	[Test]
	public void The_configured_port_is_read_and_selected_before_the_host_starts()
	{
		var path = CreateDatabase((AppPreferenceService.PublicPortKey, "9100"));

		var stored = EarlyAppPreferenceReader.TryRead(path, AppPreferenceService.PublicPortKey);
		var selection = PublicPortSelector.Resolve(null, stored, 51234);

		Assert.Multiple(() =>
		{
			Assert.That(stored, Is.EqualTo("9100"));
			Assert.That(selection.Port, Is.EqualTo(9100));
			Assert.That(selection.Source, Is.EqualTo(PublicPortSource.Preference));
		});
	}

	[Test]
	public void A_missing_key_reads_as_unset()
	{
		var path = CreateDatabase(("appearance.themeMode", "dark"));

		Assert.That(EarlyAppPreferenceReader.TryRead(path, AppPreferenceService.PublicPortKey), Is.Null);
	}

	[Test]
	public void A_missing_database_reads_as_unset_instead_of_throwing()
	{
		var path = Path.Combine(_directory, "does-not-exist.db");

		Assert.That(EarlyAppPreferenceReader.TryRead(path, AppPreferenceService.PublicPortKey), Is.Null);
	}

	[Test]
	public void A_database_without_the_preference_table_reads_as_unset()
	{
		var path = Path.Combine(_directory, "empty.db");
		using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }
			.ToString()))
		{
			connection.Open();
		}

		Assert.That(EarlyAppPreferenceReader.TryRead(path, AppPreferenceService.PublicPortKey), Is.Null);
	}
}
