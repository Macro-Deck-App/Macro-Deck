using System.Security.Cryptography;
using System.Text.Json;
using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Serilog;

namespace MacroDeckHost.Infrastructure.Backups.Restore;

public enum StagedRestoreOutcome
{
	NothingPending,
	Applied,
	Abandoned,
	RollbackIncomplete,
	Locked
}

/// <summary>
/// Applies a staged restore during startup, before anything opens the database, the Data Protection key
/// ring, the TLS key or a plugin binary. It runs before dependency injection exists, so it takes only a
/// paths object and reads its own intent file.
/// </summary>
public static class StagedRestoreApplier
{
	private const string JournalFileName = "journal.json";
	private const string LockFileName = "restore.lock";
	private const string RollbackDirectoryName = "rollback";

	private static readonly HashSet<string> _migrationHistoryTables = new(StringComparer.OrdinalIgnoreCase)
	{
		DatabaseMigrationHelper.HistoryTable,
		DatabaseMigrationHelper.LegacyHistoryTable,
		DatabaseMigrationHelper.LockTable
	};

	public static StagedRestoreOutcome ApplyPending(IMacroDeckPaths paths)
	{
		var markerPath = Path.Combine(paths.RestoreStagingDirectory, PendingRestoreDocument.FileName);
		if (!File.Exists(markerPath))
		{
			return StagedRestoreOutcome.NothingPending;
		}

		// The single-instance probe run just before this is a best-effort HTTP check, not a lock, so the
		// applier takes its own: replacing the data root while a second host is writing to it would be
		// destructive rather than merely lossy.
		FileStream lockFile;
		try
		{
			lockFile = new FileStream(Path.Combine(paths.DataRootDirectory, LockFileName),
				FileMode.OpenOrCreate,
				FileAccess.ReadWrite,
				FileShare.None);
		}
		catch (IOException)
		{
			Log.Error("A restore is staged but another process holds the data directory; skipping it");

			return StagedRestoreOutcome.Locked;
		}

		using (lockFile)
		{
			var document = ReadDocument(markerPath);
			if (document is not null && HasBegun(document))
			{
				Log.Warning("A previous start was interrupted while applying the staged restore of backup {BackupId}; "
					+ "rolling the installation back",
					document.BackupId);

				return RollBackAndAbandon(paths, markerPath, document);
			}

			if (document is null || !Validate(document))
			{
				Abandon(paths, markerPath, document);

				return StagedRestoreOutcome.Abandoned;
			}

			try
			{
				ApplyFiles(paths, document);
				ApplyTables(paths, document);
			}
			catch (Exception e)
			{
				Log.Error(e, "Applying the staged restore failed; rolling the installation back");

				return RollBackAndAbandon(paths, markerPath, document);
			}

			File.Delete(markerPath);
			TryDeleteDirectory(document.StagingDirectory);

			// The archive deliberately omits caches and staging directories, so recreate whatever the
			// swap removed before the rest of startup expects them to exist.
			paths.EnsureDirectoriesExist();

			Log.Information("Applied the staged restore of backup {BackupId}", document.BackupId);

			return StagedRestoreOutcome.Applied;
		}
	}

	public static bool HasBegun(PendingRestoreDocument document)
	{
		return File.Exists(Path.Combine(document.StagingDirectory, JournalFileName));
	}

	private static StagedRestoreOutcome RollBackAndAbandon(
		IMacroDeckPaths paths,
		string markerPath,
		PendingRestoreDocument document)
	{
		if (!RollBack(paths, document))
		{
			Log.Error("The staged restore of backup {BackupId} could not be rolled back completely; "
				+ "the next start tries again",
				document.BackupId);

			return StagedRestoreOutcome.RollbackIncomplete;
		}

		Abandon(paths, markerPath, document);

		return StagedRestoreOutcome.Abandoned;
	}

	private static PendingRestoreDocument? ReadDocument(string markerPath)
	{
		try
		{
			return JsonSerializer.Deserialize<PendingRestoreDocument>(File.ReadAllBytes(markerPath),
				PersistenceJsonOptions.Default);
		}
		catch (Exception e) when (e is JsonException or IOException)
		{
			return null;
		}
	}

	private static bool Validate(PendingRestoreDocument document)
	{
		if (!Directory.Exists(document.ApplyDirectory))
		{
			return false;
		}

		foreach (var file in document.Files)
		{
			var staged = Path.Combine(document.ApplyDirectory, ToNativePath(file.RelativePath));
			if (!File.Exists(staged) || !string.Equals(Sha256(staged), file.Sha256, StringComparison.OrdinalIgnoreCase))
			{
				Log.Error("The staged restore is incomplete at {Path}; the installation is left untouched",
					file.RelativePath);

				return false;
			}
		}

		return document.DatabasePath is null || File.Exists(document.DatabasePath);
	}

	private static void ApplyFiles(IMacroDeckPaths paths, PendingRestoreDocument document)
	{
		var selected = document.Components.ToHashSet();
		var rollbackRoot = Path.Combine(document.StagingDirectory, RollbackDirectoryName);
		Directory.CreateDirectory(rollbackRoot);

		// The journal is durable before the first file moves: a start that finds it rolls back instead of
		// validating, because from here on apply/ and the data folder may each hold only part of the files.
		WriteJournal(Path.Combine(document.StagingDirectory, JournalFileName));

		foreach (var relative in CurrentFilesOwnedBy(paths, selected, document))
		{
			var current = Path.Combine(paths.DataRootDirectory, ToNativePath(relative));
			var aside = Path.Combine(rollbackRoot, ToNativePath(relative));

			Directory.CreateDirectory(Path.GetDirectoryName(aside)!);
			File.Move(current, aside, true);
		}

		foreach (var file in SelectedFiles(document))
		{
			var staged = Path.Combine(document.ApplyDirectory, ToNativePath(file.RelativePath));
			var target = Path.Combine(paths.DataRootDirectory, ToNativePath(file.RelativePath));

			Directory.CreateDirectory(Path.GetDirectoryName(target)!);
			File.Move(staged, target, true);
		}
	}

	private static IEnumerable<PendingRestoreFile> SelectedFiles(PendingRestoreDocument document)
	{
		var selected = document.Components.ToHashSet();

		return document.Files.Where(file => selected.Contains(file.Component));
	}

	private static void ApplyTables(IMacroDeckPaths paths, PendingRestoreDocument document)
	{
		if (document.DatabasePath is null || document.Tables.Count == 0)
		{
			return;
		}

		// Pooling is off because a pooled connection keeps its file handles - the live database and the
		// staged copy attached below - open after it is disposed. On Windows that leaves the staging
		// directory, and with it a decrypted copy of the database, undeletable for the whole session.
		using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
			{ DataSource = paths.DatabasePath, Pooling = false }.ToString());
		connection.Open();

		// ATTACH cannot run inside a transaction, so the attach happens first and the whole table swap
		// then runs as one all-or-nothing transaction.
		Execute(connection, $"ATTACH DATABASE '{document.DatabasePath.Replace("'", "''")}' AS backup;");

		try
		{
			using var transaction = connection.BeginTransaction();
			var restoresSecrets = document.Tables.Contains("secret", StringComparer.OrdinalIgnoreCase);
			var localRecoveryKey = restoresSecrets ? RecoveryKeySecret(connection, transaction, "main") : null;

			foreach (var table in document.Tables)
			{
				if (_migrationHistoryTables.Contains(table))
				{
					continue;
				}

				var columns = SharedColumns(connection, table);
				if (columns.Count == 0)
				{
					continue;
				}

				var list = string.Join(", ", columns);
				var filter = table == "app_preference" ? PreferenceFilter() : string.Empty;

				Execute(connection, $"DELETE FROM main.{table}{filter};", transaction);
				Execute(connection,
					$"INSERT INTO main.{table} ({list}) SELECT {list} FROM backup.{table}{filter};",
					transaction);
			}

			if (restoresSecrets)
			{
				AdoptArchiveRecoveryKeyPointer(connection, transaction, localRecoveryKey);
			}

			transaction.Commit();
		}
		finally
		{
			Execute(connection, "DETACH DATABASE backup;");
		}
	}

	// Keys that must survive a restore from a foreign installation: its own identity, and the pointer to its
	// recovery key unless the secret table is restored too.
	private static string PreferenceFilter()
	{
		var conditions = BackupComponentGroups.PreferenceKeyDenyPrefixes
			.Select(prefix => $"ap_key NOT LIKE '{prefix}%'");

		return " WHERE " + string.Join(" AND ", conditions);
	}

	private static void AdoptArchiveRecoveryKeyPointer(
		SqliteConnection connection,
		SqliteTransaction transaction,
		(string Pointer, string Value)? localRecoveryKey)
	{
		var columns = SharedColumns(connection, "app_preference");
		var archiveRecoveryKey = RecoveryKeySecret(connection, transaction, "backup");
		if (columns.Count == 0 || archiveRecoveryKey is null || Same(archiveRecoveryKey, localRecoveryKey))
		{
			return;
		}

		var list = string.Join(", ", columns);
		var match = $"ap_key LIKE '{BackupComponentGroups.RecoveryKeyPreferencePrefix}%'";

		Execute(connection, $"DELETE FROM main.app_preference WHERE {match};", transaction);
		Execute(connection,
			$"INSERT INTO main.app_preference ({list}) SELECT {list} FROM backup.app_preference WHERE {match};",
			transaction);
	}

	private static bool Same((string Pointer, string Value)? archive, (string Pointer, string Value)? local)
	{
		return local is not null
			&& string.Equals(archive!.Value.Pointer, local.Value.Pointer, StringComparison.OrdinalIgnoreCase)
			&& string.Equals(archive.Value.Value, local.Value.Value, StringComparison.Ordinal);
	}

	private static (string Pointer, string Value)? RecoveryKeySecret(
		SqliteConnection connection,
		SqliteTransaction transaction,
		string schema)
	{
		if (ColumnsOf(connection, schema, "app_preference").Count == 0
			|| ColumnsOf(connection, schema, "secret").Count == 0)
		{
			return null;
		}

		using var command = connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandText =
			$"SELECT p.ap_value, s.s_encrypted_value FROM {schema}.app_preference p JOIN {schema}.secret s "
			+ "ON lower(s.s_id) = lower(p.ap_value) WHERE p.ap_key = $pointer;";
		command.Parameters.AddWithValue("$pointer", AppPreferenceService.BackupRecoveryKeySecretIdKey);

		using var reader = command.ExecuteReader();
		return reader.Read() ? (reader.GetString(0), reader.GetString(1)) : null;
	}

	private static List<string> SharedColumns(SqliteConnection connection, string table)
	{
		var main = ColumnsOf(connection, "main", table);
		var backup = ColumnsOf(connection, "backup", table);

		return [.. main.Where(backup.Contains)];
	}

	private static List<string> ColumnsOf(SqliteConnection connection, string schema, string table)
	{
		var columns = new List<string>();

		using var command = connection.CreateCommand();
		command.CommandText = $"PRAGMA {schema}.table_info({table});";

		try
		{
			using var reader = command.ExecuteReader();
			while (reader.Read())
			{
				columns.Add(reader.GetString(1));
			}
		}
		catch (SqliteException)
		{
			return [];
		}

		return columns;
	}

	private static IEnumerable<string> CurrentFilesOwnedBy(
		IMacroDeckPaths paths,
		HashSet<Domain.Enums.BackupComponentGroup> selected,
		PendingRestoreDocument document)
	{
		var archived = document.Files.Select(file => file.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);

		if (!Directory.Exists(paths.DataRootDirectory))
		{
			yield break;
		}

		foreach (var absolute in Directory.EnumerateFiles(paths.DataRootDirectory, "*", SearchOption.AllDirectories))
		{
			var relative = Path.GetRelativePath(paths.DataRootDirectory, absolute).Replace('\\', '/');
			if (BackupComponentGroups.IsExcluded(relative))
			{
				continue;
			}

			if (BackupComponentGroups.KeptWhenMissingFromArchive.Contains(relative, StringComparer.OrdinalIgnoreCase)
				&& !archived.Contains(relative))
			{
				continue;
			}

			var owner = BackupComponentGroups.Owner(relative);
			if (owner is not null && selected.Contains(owner.Value))
			{
				yield return relative;
			}
		}
	}

	// Must stay safe to rerun after being killed itself: every restored file goes back to apply/ before
	// any original returns over it.
	private static bool RollBack(IMacroDeckPaths paths, PendingRestoreDocument document)
	{
		var failures = 0;

		foreach (var file in SelectedFiles(document))
		{
			var staged = Path.Combine(document.ApplyDirectory, ToNativePath(file.RelativePath));
			var target = Path.Combine(paths.DataRootDirectory, ToNativePath(file.RelativePath));
			if (File.Exists(staged) || !File.Exists(target))
			{
				continue;
			}

			if (!TryMove(target, staged, file.RelativePath))
			{
				failures++;
			}
		}

		if (failures > 0)
		{
			return false;
		}

		var rollbackRoot = Path.Combine(document.StagingDirectory, RollbackDirectoryName);
		if (!Directory.Exists(rollbackRoot))
		{
			return true;
		}

		foreach (var aside in Directory.GetFiles(rollbackRoot, "*", SearchOption.AllDirectories))
		{
			var relative = Path.GetRelativePath(rollbackRoot, aside);
			if (!TryMove(aside, Path.Combine(paths.DataRootDirectory, relative), relative))
			{
				failures++;
			}
		}

		return failures == 0;
	}

	private static bool TryMove(string source, string destination, string relativePath)
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
			File.Move(source, destination, true);

			return true;
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			Log.Error(e, "Could not roll {Path} back", relativePath);

			return false;
		}
	}

	private static void Abandon(IMacroDeckPaths paths, string markerPath, PendingRestoreDocument? document)
	{
		try
		{
			File.Delete(markerPath);

			if (document is not null && Directory.Exists(document.StagingDirectory))
			{
				var failed = document.StagingDirectory
					+ ".failed-"
					+ DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", System.Globalization.CultureInfo.InvariantCulture);
				Directory.Move(document.StagingDirectory, failed);
			}
		}
		catch (IOException e)
		{
			Log.Error(e, "Could not clean up the abandoned restore staging directory");
		}

		Log.Warning("A staged restore was abandoned; the installation was left as it was");
	}

	private static void WriteJournal(string path)
	{
		using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
		stream.Write("{}"u8);
		stream.Flush(true);
	}

	private static void Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
	{
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		command.Transaction = transaction;
		command.ExecuteNonQuery();
	}

	private static string Sha256(string path)
	{
		using var stream = File.OpenRead(path);

		return Convert.ToHexStringLower(SHA256.HashData(stream));
	}

	private static string ToNativePath(string relativePath)
	{
		return relativePath.Replace('/', Path.DirectorySeparatorChar);
	}

	private static void TryDeleteDirectory(string path)
	{
		try
		{
			if (Directory.Exists(path))
			{
				Directory.Delete(path, true);
			}
		}
		catch (IOException)
		{
		}
	}
}
