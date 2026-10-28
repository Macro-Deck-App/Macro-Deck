using System.Globalization;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Persistence;

[TestFixture]
[NonParallelizable]
public class DatabaseMigrationTests
{
	private const string UserId = "AAAAAAAA-0000-0000-0000-000000000001";
	private const string FirstTokenId = "BBBBBBBB-0000-0000-0000-000000000001";
	private const string RotatedTokenId = "BBBBBBBB-0000-0000-0000-000000000002";
	private const string RegistrationId = "CCCCCCCC-0000-0000-0000-000000000001";
	private const string NewerMigrationId = "99991231235959_FromANewerMacroDeck";

	private static readonly Version _pluginRegistryVersion = new(1, 9, 0, 0);

	private string _directory = null!;
	private CultureInfo _culture = null!;
	private CultureInfo _uiCulture = null!;

	[SetUp]
	public void SetUp()
	{
		_directory = Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_directory);
		_culture = CultureInfo.CurrentCulture;
		_uiCulture = CultureInfo.CurrentUICulture;
	}

	[TearDown]
	public void TearDown()
	{
		CultureInfo.CurrentCulture = _culture;
		CultureInfo.CurrentUICulture = _uiCulture;
		SqliteConnection.ClearAllPools();

		if (Directory.Exists(_directory))
		{
			Directory.Delete(_directory, true);
		}
	}

	[Test]
	public void A_fresh_database_is_created_under_a_thai_regional_format()
	{
		UseThaiCulture();
		var path = DatabasePath("fresh");

		DatabaseMigrationHelper.Migrate(path);

		Assert.That(AppliedMigrations(path), Is.EqualTo(ShippedMigrations()));
	}

	[Test]
	public void The_initial_ef_core_migration_matches_the_schema_the_evolve_scripts_built()
	{
		var legacy = DatabasePath("legacy");
		var fresh = DatabasePath("fresh");
		LegacyEvolveDatabase.Create(legacy, LegacyEvolveDatabase.Newest);

		using (var context = new DatabaseContext(Log.Logger, fresh))
		{
			context.GetService<IMigrator>().Migrate(InitialMigration());
		}

		Assert.That(LegacyEvolveDatabase.DescribeSchema(fresh),
			Is.EqualTo(LegacyEvolveDatabase.DescribeSchema(legacy)));
	}

	[Test]
	public void A_database_left_behind_by_the_failed_thai_start_gets_the_full_schema()
	{
		UseThaiCulture();
		var path = DatabasePath("reporter");
		LegacyEvolveDatabase.Create(path, null);

		DatabaseMigrationHelper.Migrate(path);

		Assert.Multiple(() =>
		{
			Assert.That(LegacyEvolveDatabase.DescribeSchema(path), Is.EqualTo(FreshSchema()));
			Assert.That(AppliedMigrations(path), Is.EqualTo(ShippedMigrations()));
		});
	}

	[Test]
	public void A_database_at_the_newest_evolve_script_gains_the_history_and_the_later_migrations_only()
	{
		var path = DatabasePath("current");
		LegacyEvolveDatabase.Create(path, LegacyEvolveDatabase.Newest);
		SeedUser(path);
		var evolveHistoryBefore = EvolveHistory(path);

		DatabaseMigrationHelper.Migrate(path);

		Assert.Multiple(() =>
		{
			Assert.That(LegacyEvolveDatabase.DescribeSchema(path), Is.EqualTo(FreshSchema()));
			Assert.That(EvolveHistory(path), Is.EqualTo(evolveHistoryBefore));
			Assert.That(AppliedMigrations(path), Is.EqualTo(ShippedMigrations()));
			Assert.That(LegacyEvolveDatabase.Query(path, "SELECT u_username FROM app_user;"), Is.EqualTo(["admin"]));
		});
	}

	[TestCase("en-US")]
	[TestCase("th-TH")]
	public void A_database_at_an_older_evolve_script_is_brought_up_to_date_with_its_data(string culture)
	{
		CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
		CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
		var path = DatabasePath("older");
		LegacyEvolveDatabase.Create(path, _pluginRegistryVersion);
		SeedUser(path);
		SeedRotatedTokens(path);
		SeedPluginRegistration(path);

		DatabaseMigrationHelper.Migrate(path);

		Assert.Multiple(() =>
		{
			Assert.That(LegacyEvolveDatabase.DescribeSchema(path), Is.EqualTo(FreshSchema()));
			Assert.That(AppliedMigrations(path), Is.EqualTo(ShippedMigrations()));
			Assert.That(LegacyEvolveDatabase.Query(path, "SELECT u_username FROM app_user;"), Is.EqualTo(["admin"]));
			Assert.That(LegacyEvolveDatabase.Query(path,
					"SELECT rt_id, rt_family_id FROM refresh_token ORDER BY rt_id;"),
				Is.EqualTo([$"{FirstTokenId}|{FirstTokenId}", $"{RotatedTokenId}|{FirstTokenId}"]));
			Assert.That(LegacyEvolveDatabase.Query(path, "SELECT pr_display_name, pr_origin FROM plugin_registration;"),
				Is.EqualTo(["Demo|developer-token"]));
		});
	}

	[Test]
	public void A_plugin_trust_record_from_before_signers_were_recorded_keeps_its_admission_without_a_signer()
	{
		var path = DatabasePath("trust");
		LegacyEvolveDatabase.Create(path, LegacyEvolveDatabase.Newest);
		LegacyEvolveDatabase.Execute(path,
			"INSERT INTO plugin_trust_record (ptr_id, ptr_plugin_id, ptr_version, ptr_admitted_verdict, "
			+ "ptr_certificate_id, ptr_installed_at, ptr_created_at) "
			+ $"VALUES ('{RegistrationId}', 'demo.plugin', '1.0.0', 'Trusted', 'cert_1', '2026-01-01', '2026-01-01');");

		DatabaseMigrationHelper.Migrate(path);

		Assert.That(LegacyEvolveDatabase.Query(path,
				"SELECT ptr_admitted_verdict, ptr_certificate_id, ptr_signer_kind, ptr_signer_id "
				+ "FROM plugin_trust_record;"),
			Is.EqualTo(["Trusted|cert_1|null|null"]));
	}

	[Test]
	public void A_failed_evolve_attempt_does_not_count_as_applied()
	{
		var path = DatabasePath("failed");
		var lastSucceeded = LegacyEvolveMigrations.Scripts[^2].Version;
		LegacyEvolveDatabase.Create(path, lastSucceeded);
		LegacyEvolveDatabase.Record(path, LegacyEvolveDatabase.Newest, false);

		DatabaseMigrationHelper.Migrate(path);

		Assert.That(LegacyEvolveDatabase.DescribeSchema(path), Is.EqualTo(FreshSchema()));
	}

	[Test]
	public void An_applied_version_without_a_shipped_script_is_ignored()
	{
		var path = DatabasePath("early-beta");
		LegacyEvolveDatabase.Create(path, LegacyEvolveDatabase.Newest);
		LegacyEvolveDatabase.Record(path, new Version(1, 1, 0, 0), true);

		DatabaseMigrationHelper.Migrate(path);

		Assert.That(AppliedMigrations(path), Is.EqualTo(ShippedMigrations()));
	}

	[Test]
	public void A_database_from_a_newer_evolve_history_is_refused_untouched()
	{
		var path = DatabasePath("newer");
		LegacyEvolveDatabase.Create(path, LegacyEvolveDatabase.Newest);
		LegacyEvolveDatabase.Record(path, new Version(LegacyEvolveDatabase.Newest.Major, 999, 0, 0), true);

		Assert.Multiple(() =>
		{
			Assert.That(() => DatabaseMigrationHelper.Migrate(path), Throws.TypeOf<DatabaseMigrationException>());
			Assert.That(LegacyEvolveDatabase.Tables(path), Does.Not.Contain(DatabaseMigrationHelper.HistoryTable));
		});
	}

	[Test]
	public void A_database_migrated_by_a_newer_host_is_refused_untouched()
	{
		var path = DatabasePath("newer-ef");
		DatabaseMigrationHelper.Migrate(path);
		LegacyEvolveDatabase.Execute(path,
			$"INSERT INTO \"{DatabaseMigrationHelper.HistoryTable}\" (\"MigrationId\", \"ProductVersion\") "
			+ $"VALUES ('{NewerMigrationId}', '99.0.0');");
		LegacyEvolveDatabase.Execute(path,
			$"INSERT INTO \"{DatabaseMigrationHelper.LockTable}\" (\"Id\", \"Timestamp\") VALUES (1, '2026-01-01');");
		var historyBefore = AppliedMigrations(path);
		var schemaBefore = LegacyEvolveDatabase.DescribeSchema(path);

		Assert.Multiple(() =>
		{
			Assert.That(() => DatabaseMigrationHelper.Migrate(path),
				Throws.TypeOf<DatabaseMigrationException>().With.Message.Contains(NewerMigrationId));
			Assert.That(AppliedMigrations(path), Is.EqualTo(historyBefore));
			Assert.That(LegacyEvolveDatabase.DescribeSchema(path), Is.EqualTo(schemaBefore));
			Assert.That(LegacyEvolveDatabase.Query(path,
					$"SELECT count(*) FROM \"{DatabaseMigrationHelper.LockTable}\";"),
				Is.EqualTo(["1"]),
				"a refused database is left exactly as the newer host wrote it");
		});
	}

	[Test]
	public void A_history_that_skips_a_shipped_script_is_refused()
	{
		var path = DatabasePath("gap");
		LegacyEvolveDatabase.Create(path, LegacyEvolveDatabase.Newest);
		LegacyEvolveDatabase.Execute(path,
			$"DELETE FROM database_migration_history WHERE version = '{_pluginRegistryVersion}';");

		Assert.That(() => DatabaseMigrationHelper.Migrate(path), Throws.TypeOf<DatabaseMigrationException>());
	}

	[Test]
	public void A_migration_lock_left_by_a_killed_host_does_not_block_the_next_start()
	{
		var path = DatabasePath("locked");
		DatabaseMigrationHelper.Migrate(path);
		LegacyEvolveDatabase.Execute(path,
			$"INSERT INTO \"{DatabaseMigrationHelper.LockTable}\" (\"Id\", \"Timestamp\") VALUES (1, '2026-01-01');");

		var migration = Task.Run(() => DatabaseMigrationHelper.Migrate(path));

		Assert.That(migration.Wait(TimeSpan.FromSeconds(30)), Is.True, "the migration waited on a lock nobody holds");
	}

	[Test]
	public void The_model_has_no_changes_without_a_migration()
	{
		var path = DatabasePath("model");
		using var context = new DatabaseContext(Log.Logger, path);

		Assert.That(context.Database.HasPendingModelChanges(), Is.False);
	}

	private static void UseThaiCulture()
	{
		CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("th-TH");
		CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("th-TH");
	}

	private string DatabasePath(string name)
	{
		return Path.Combine(_directory, name + ".db");
	}

	private List<string> FreshSchema()
	{
		var path = DatabasePath("reference-" + Guid.NewGuid().ToString("N"));
		DatabaseMigrationHelper.Migrate(path);
		return LegacyEvolveDatabase.DescribeSchema(path);
	}

	private static List<string> AppliedMigrations(string path)
	{
		return LegacyEvolveDatabase.Query(path,
			$"SELECT \"MigrationId\" FROM \"{DatabaseMigrationHelper.HistoryTable}\" ORDER BY \"MigrationId\";");
	}

	private List<string> ShippedMigrations()
	{
		using var context = new DatabaseContext(Log.Logger, DatabasePath("unused"));
		return [.. context.Database.GetMigrations().Order(StringComparer.Ordinal)];
	}

	private string InitialMigration()
	{
		return ShippedMigrations().Single(migration => migration.EndsWith("_Initial", StringComparison.Ordinal));
	}

	private static List<string> EvolveHistory(string path)
	{
		return LegacyEvolveDatabase.Query(path, "SELECT * FROM database_migration_history ORDER BY id;");
	}

	private static void SeedUser(string path)
	{
		LegacyEvolveDatabase.Execute(path,
			"INSERT INTO app_user (u_id, u_username, u_password_hash, u_created_at, u_updated_at) "
			+ $"VALUES ('{UserId}', 'admin', 'hash', '2026-01-01', '2026-01-01');");
	}

	private static void SeedRotatedTokens(string path)
	{
		LegacyEvolveDatabase.Execute(path,
			"INSERT INTO refresh_token (rt_id, rt_user_id, rt_token_hash, rt_scope, rt_expires_at, "
			+ "rt_replaced_by_id, rt_created_at) VALUES "
			+ $"('{FirstTokenId}', '{UserId}', 'hash-1', 1, '2030-01-01', '{RotatedTokenId}', '2026-01-01'), "
			+ $"('{RotatedTokenId}', '{UserId}', 'hash-2', 1, '2030-01-01', NULL, '2026-01-02');");
	}

	private static void SeedPluginRegistration(string path)
	{
		LegacyEvolveDatabase.Execute(path,
			"INSERT INTO plugin_registration (pr_id, pr_plugin_id, pr_display_name, pr_secret_hash, "
			+ "pr_access_token_id, pr_created_at) "
			+ $"VALUES ('{RegistrationId}', 'demo.plugin', 'Demo', 'hash', '{UserId}', '2026-01-01');");
	}
}
