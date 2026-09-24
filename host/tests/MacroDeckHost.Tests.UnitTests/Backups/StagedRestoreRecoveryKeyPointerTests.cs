using System.Text.Json;
using System.Text.Json.Serialization;
using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Infrastructure.Backups.Restore;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Data.Sqlite;

namespace MacroDeckHost.Tests.UnitTests.Backups;

[TestFixture]
public class StagedRestoreRecoveryKeyPointerTests
{
	private const string LocalSecretId = "11111111-1111-1111-1111-111111111111";
	private const string ArchiveSecretId = "22222222-2222-2222-2222-222222222222";

	private TestPaths _paths = null!;
	private string _archiveDatabase = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		_paths.EnsureDirectoriesExist();
		_archiveDatabase = Path.Combine(_paths.RestoreStagingDirectory, "archive.db");

		CreateDatabase(_paths.DatabasePath, LocalSecretId, "local");
		CreateDatabase(_archiveDatabase, ArchiveSecretId, "archive");
	}

	[TearDown]
	public void TearDown()
	{
		SqliteConnection.ClearAllPools();
		_paths.Cleanup();
	}

	[Test]
	public void Restoring_the_secrets_without_app_settings_takes_the_archive_recovery_key()
	{
		Apply(["secret", "integration_config_entry"]);

		Assert.Multiple(() =>
		{
			Assert.That(Preference(AppPreferenceService.BackupRecoveryKeySecretIdKey), Is.EqualTo(ArchiveSecretId));
			Assert.That(Preference(AppPreferenceService.BackupRecoveryKeyExportedAtKey), Is.EqualTo("archive-exported"));
			Assert.That(Preference("theme"), Is.EqualTo("local"), "app settings were not part of this restore");
			Assert.That(Preference("telemetry.installationId"), Is.EqualTo("local"));
		});
	}

	[Test]
	public void Restoring_secrets_and_app_settings_takes_the_archive_recovery_key_but_keeps_the_identity()
	{
		Apply(["secret", "app_preference"]);

		Assert.Multiple(() =>
		{
			Assert.That(Preference(AppPreferenceService.BackupRecoveryKeySecretIdKey), Is.EqualTo(ArchiveSecretId));
			Assert.That(Preference(AppPreferenceService.BackupRecoveryKeyCreatedAtKey), Is.EqualTo("archive-created"));
			Assert.That(Preference("theme"), Is.EqualTo("archive"));
			Assert.That(Preference("telemetry.installationId"), Is.EqualTo("local"));
			Assert.That(Preference("onboarding.completed"), Is.EqualTo("local"));
		});
	}

	[Test]
	public void Restoring_app_settings_without_the_secrets_keeps_this_installation_recovery_key()
	{
		Apply(["app_preference"]);

		Assert.Multiple(() =>
		{
			Assert.That(Preference(AppPreferenceService.BackupRecoveryKeySecretIdKey), Is.EqualTo(LocalSecretId));
			Assert.That(Preference(AppPreferenceService.BackupRecoveryKeyExportedAtKey), Is.EqualTo("local-exported"));
			Assert.That(Preference("theme"), Is.EqualTo("archive"));
		});
	}

	[TestCase(null)]
	[TestCase("33333333-3333-3333-3333-333333333333")]
	public void An_archive_pointer_that_names_no_archived_secret_is_not_taken(string? archivePointer)
	{
		SetArchivePointer(archivePointer);

		Apply(["secret", "app_preference"]);

		Assert.That(Preference(AppPreferenceService.BackupRecoveryKeySecretIdKey), Is.EqualTo(LocalSecretId));
	}

	[Test]
	public void Restoring_a_backup_made_with_the_same_recovery_key_keeps_its_saved_state()
	{
		using (var connection = Open(_archiveDatabase))
		{
			Execute(connection, "DELETE FROM secret;");
			Execute(connection, $"INSERT INTO secret VALUES ('{LocalSecretId}', 0, 'local-key', 'x', 'x');");
		}
		SetArchivePointer(LocalSecretId);

		Apply(["secret", "app_preference"]);

		Assert.That(Preference(AppPreferenceService.BackupRecoveryKeyExportedAtKey), Is.EqualTo("local-exported"),
			"the key did not change, so neither did whether the user has saved it");
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
			JsonSerializer.Serialize(document, Json));

		Assert.That(StagedRestoreApplier.ApplyPending(_paths), Is.EqualTo(StagedRestoreOutcome.Applied));
	}

	private static void CreateDatabase(string path, string secretId, string origin)
	{
		using var connection = Open(path);
		Execute(connection,
			"""
			CREATE TABLE secret (s_id TEXT PRIMARY KEY, s_kind INTEGER NOT NULL, s_encrypted_value TEXT NOT NULL,
				s_created_at TEXT NOT NULL, s_updated_at TEXT NOT NULL);
			CREATE TABLE app_preference (ap_key TEXT PRIMARY KEY, ap_value TEXT NOT NULL,
				ap_created_at TEXT NOT NULL, ap_updated_at TEXT NOT NULL);
			CREATE TABLE integration_config_entry (id TEXT PRIMARY KEY, value TEXT NOT NULL);
			""");
		Execute(connection, $"INSERT INTO secret VALUES ('{secretId}', 0, '{origin}-key', 'x', 'x');");
		InsertPreference(connection, AppPreferenceService.BackupRecoveryKeySecretIdKey, secretId);
		InsertPreference(connection, AppPreferenceService.BackupRecoveryKeyCreatedAtKey, $"{origin}-created");
		InsertPreference(connection, AppPreferenceService.BackupRecoveryKeyExportedAtKey, $"{origin}-exported");
		InsertPreference(connection, "theme", origin);
		InsertPreference(connection, "telemetry.installationId", origin);
		InsertPreference(connection, "onboarding.completed", origin);
	}

	private void SetArchivePointer(string? value)
	{
		using var connection = Open(_archiveDatabase);
		Execute(connection,
			$"DELETE FROM app_preference WHERE ap_key = '{AppPreferenceService.BackupRecoveryKeySecretIdKey}';");
		if (value is not null)
		{
			InsertPreference(connection, AppPreferenceService.BackupRecoveryKeySecretIdKey, value);
		}
	}

	private string? Preference(string key)
	{
		using var connection = Open(_paths.DatabasePath);
		using var command = connection.CreateCommand();
		command.CommandText = "SELECT ap_value FROM app_preference WHERE ap_key = $key;";
		command.Parameters.AddWithValue("$key", key);

		return command.ExecuteScalar() as string;
	}

	private static void InsertPreference(SqliteConnection connection, string key, string value)
	{
		using var command = connection.CreateCommand();
		command.CommandText = "INSERT INTO app_preference VALUES ($key, $value, 'x', 'x');";
		command.Parameters.AddWithValue("$key", key);
		command.Parameters.AddWithValue("$value", value);
		command.ExecuteNonQuery();
	}

	private static SqliteConnection Open(string path)
	{
		var connection = new SqliteConnection(
			new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
		connection.Open();

		return connection;
	}

	private static void Execute(SqliteConnection connection, string sql)
	{
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		command.ExecuteNonQuery();
	}

	private static readonly JsonSerializerOptions Json =
		new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
}
