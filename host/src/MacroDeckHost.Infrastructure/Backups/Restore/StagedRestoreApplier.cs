using System.Security.Cryptography;
using System.Text.Json;
using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Infrastructure.Mcp;
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
	internal const string LockFileName = "restore.lock";
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
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			Log.Error(e, "A restore is staged but the data directory cannot be locked; skipping it");

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

			if (document is null || !Validate(paths, document))
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

			CompleteApply(paths, markerPath, document);
			McpStateStore.DeleteJournal(paths);

			Log.Information("Applied the staged restore of backup {BackupId}", document.BackupId);

			return StagedRestoreOutcome.Applied;
		}
	}

	public static bool HasBegun(PendingRestoreDocument document)
	{
		return File.Exists(Path.Combine(document.StagingDirectory, JournalFileName));
	}

	private static void CompleteApply(IMacroDeckPaths paths, string markerPath, PendingRestoreDocument document)
	{
		// The journal goes first: a marker left without it fails validation and is abandoned on the next
		// start, while marker and journal together would roll the files back over the committed tables.
		var journalDeleted = TryDeleteFile(Path.Combine(document.StagingDirectory, JournalFileName));
		if (!TryDeleteFile(markerPath) && !journalDeleted)
		{
			Log.Error("The applied restore of backup {BackupId} could not be marked complete; "
				+ "the next start rolls its files back",
				document.BackupId);
		}

		TryDeleteDirectory(document.StagingDirectory);

		// The archive deliberately omits caches and staging directories, so recreate whatever the
		// swap removed before the rest of startup expects them to exist.
		try
		{
			paths.EnsureDirectoriesExist();
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			Log.Error(e, "Could not recreate the data directories after the restore");
		}
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
		catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	private static bool Validate(IMacroDeckPaths paths, PendingRestoreDocument document)
	{
		if (!Directory.Exists(document.ApplyDirectory))
		{
			return false;
		}

		foreach (var file in document.Files)
		{
			var staged = BackupArchiveLimits.ResolveInside(document.ApplyDirectory, file.RelativePath);
			if (!BackupArchiveLimits.IsRestorableEntry(file.RelativePath, file.Component)
				|| staged is null
				|| BackupArchiveLimits.ResolveInside(paths.DataRootDirectory, file.RelativePath) is null)
			{
				Log.Error("The staged restore names {Path} for {Component}, which a restore may not write; "
					+ "the installation is left untouched",
					file.RelativePath,
					file.Component);

				return false;
			}

			if (!MatchesStagedHash(staged, file.Sha256))
			{
				Log.Error("The staged restore is incomplete at {Path}; the installation is left untouched",
					file.RelativePath);

				return false;
			}
		}

		return document.DatabasePath is null || File.Exists(document.DatabasePath);
	}

	private static bool MatchesStagedHash(string staged, string sha256)
	{
		try
		{
			return File.Exists(staged) && string.Equals(Sha256(staged), sha256, StringComparison.OrdinalIgnoreCase);
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			Log.Error(e, "Could not read the staged file {Path}", staged);

			return false;
		}
	}

	private static void ApplyFiles(IMacroDeckPaths paths, PendingRestoreDocument document)
	{
		var selected = document.Components.ToHashSet();
		var rollbackRoot = Path.Combine(document.StagingDirectory, RollbackDirectoryName);
		Directory.CreateDirectory(rollbackRoot);

		// The journal is durable before the first file moves: a start that finds it rolls back instead of
		// validating, because from here on apply/ and the data folder may each hold only part of the files.
		WriteJournal(Path.Combine(document.StagingDirectory, JournalFileName),
			new RestoreJournal { CreatedDirectories = MissingDirectories(paths, SelectedFiles(document)) });

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

	private static List<string> MissingDirectories(IMacroDeckPaths paths, IEnumerable<PendingRestoreFile> files)
	{
		var missing = new HashSet<string>(StringComparer.Ordinal);

		foreach (var relative in files.Select(file => file.RelativePath))
		{
			for (var slash = relative.LastIndexOf('/'); slash > 0; slash = relative.LastIndexOf('/', slash - 1))
			{
				var directory = relative[..slash];
				if (Directory.Exists(Path.Combine(paths.DataRootDirectory, ToNativePath(directory))))
				{
					break;
				}

				missing.Add(directory);
			}
		}

		return [.. missing];
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
	// any original returns over it. Created folders go in between, an original may need the same path.
	private static bool RollBack(IMacroDeckPaths paths, PendingRestoreDocument document)
	{
		if (!ReturnRestoredFiles(paths, document))
		{
			return false;
		}

		var directoriesRemoved = RemoveCreatedDirectories(paths, document);

		return ReturnOriginals(paths, document) && directoriesRemoved;
	}

	private static bool ReturnRestoredFiles(IMacroDeckPaths paths, PendingRestoreDocument document)
	{
		var failures = 0;

		foreach (var file in SelectedFiles(document))
		{
			var staged = BackupArchiveLimits.ResolveInside(document.ApplyDirectory, file.RelativePath);
			var target = BackupArchiveLimits.ResolveInside(paths.DataRootDirectory, file.RelativePath);
			if (staged is null || target is null || File.Exists(staged) || !File.Exists(target))
			{
				continue;
			}

			if (!TryMove(target, staged, file.RelativePath))
			{
				failures++;
			}
		}

		return failures == 0;
	}

	private static bool ReturnOriginals(IMacroDeckPaths paths, PendingRestoreDocument document)
	{
		var rollbackRoot = Path.Combine(document.StagingDirectory, RollbackDirectoryName);
		string[] originals;
		try
		{
			originals = Directory.Exists(rollbackRoot)
				? Directory.GetFiles(rollbackRoot, "*", SearchOption.AllDirectories)
				: [];
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			Log.Error(e, "Could not list the files the restore set aside");

			return false;
		}

		var failures = 0;

		foreach (var aside in originals)
		{
			var relative = Path.GetRelativePath(rollbackRoot, aside);
			if (!TryMove(aside, Path.Combine(paths.DataRootDirectory, relative), relative))
			{
				failures++;
			}
		}

		return failures == 0;
	}

	private static bool RemoveCreatedDirectories(IMacroDeckPaths paths, PendingRestoreDocument document)
	{
		var journal = ReadJournal(Path.Combine(document.StagingDirectory, JournalFileName));
		if (journal is null)
		{
			return false;
		}

		var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(paths.DataRootDirectory))
			+ Path.DirectorySeparatorChar;
		var failures = 0;

		foreach (var relative in journal.CreatedDirectories.OrderByDescending(directory => directory.Length))
		{
			var absolute = Path.GetFullPath(Path.Combine(paths.DataRootDirectory, ToNativePath(relative)));
			if (!absolute.StartsWith(root, StringComparison.Ordinal) || !Directory.Exists(absolute))
			{
				continue;
			}

			try
			{
				if (Directory.EnumerateFileSystemEntries(absolute).Any())
				{
					Log.Warning("Kept the folder {Path}: it holds content the restore did not put there", relative);

					continue;
				}

				Directory.Delete(absolute);
			}
			catch (Exception e) when (e is IOException or UnauthorizedAccessException)
			{
				Log.Error(e, "Could not remove the folder {Path} the restore created", relative);
				failures++;
			}
		}

		return failures == 0;
	}

	private static RestoreJournal? ReadJournal(string path)
	{
		try
		{
			if (!File.Exists(path))
			{
				return new RestoreJournal();
			}

			return JsonSerializer.Deserialize<RestoreJournal>(File.ReadAllBytes(path), PersistenceJsonOptions.Default)
				?? new RestoreJournal();
		}
		catch (JsonException)
		{
			// A journal from an older host or one cut short by a kill records no folders.
			return new RestoreJournal();
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			Log.Error(e, "Could not read the restore journal");

			return null;
		}
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
		TryDeleteFile(markerPath);

		if (document is not null && Directory.Exists(document.StagingDirectory))
		{
			var failed = document.StagingDirectory
				+ ".failed-"
				+ DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", System.Globalization.CultureInfo.InvariantCulture);
			try
			{
				Directory.Move(document.StagingDirectory, failed);
			}
			catch (Exception e) when (e is IOException or UnauthorizedAccessException)
			{
				Log.Error(e,
					"Could not set the abandoned restore staging directory {Path} aside",
					document.StagingDirectory);
			}
		}

		Log.Warning("A staged restore was abandoned; the installation was left as it was");
	}

	private static void WriteJournal(string path, RestoreJournal journal)
	{
		using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
		stream.Write(JsonSerializer.SerializeToUtf8Bytes(journal, PersistenceJsonOptions.Default));
		stream.Flush(true);
	}

	private static bool TryDeleteFile(string path)
	{
		try
		{
			File.Delete(path);

			return true;
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			Log.Error(e, "Could not delete {Path}", path);

			return false;
		}
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
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			Log.Warning(e, "Could not delete the restore staging directory {Path}", path);
		}
	}

	private sealed class RestoreJournal
	{
		public List<string> CreatedDirectories { get; init; } = [];
	}
}
