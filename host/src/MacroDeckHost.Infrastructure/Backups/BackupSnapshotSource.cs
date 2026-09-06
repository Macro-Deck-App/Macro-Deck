using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace MacroDeckHost.Infrastructure.Backups;

public sealed class BackupSnapshotSource : IBackupSnapshotSource
{
	private readonly IMacroDeckPaths _paths;

	public BackupSnapshotSource(IMacroDeckPaths paths) => _paths = paths;

	public BackupSnapshotPlan Plan()
	{
		var files = new List<BackupSnapshotFile>();
		var skipped = new List<BackupSkippedEntry>();
		var total = 0L;

		foreach (var absolute in Directory.EnumerateFiles(_paths.DataRootDirectory, "*", SearchOption.AllDirectories))
		{
			var relative = Path.GetRelativePath(_paths.DataRootDirectory, absolute).Replace('\\', '/');

			if (BackupComponentGroups.IsExcluded(relative) || IsUnwantedSidecar(relative))
			{
				continue;
			}

			var owner = BackupComponentGroups.Owner(relative);
			if (owner is null)
			{
				continue;
			}

			try
			{
				total += new FileInfo(absolute).Length;
				files.Add(new BackupSnapshotFile(relative, absolute, owner.Value));
			}
			catch (IOException e)
			{
				skipped.Add(new BackupSkippedEntry { Path = relative, Reason = e.Message });
			}
		}

		return new BackupSnapshotPlan(files, skipped, total);
	}

	public async Task<string> CopyDatabase(string destinationPath, CancellationToken cancellationToken = default)
	{
		await using var source = new SqliteConnection(Unpooled(_paths.DatabasePath));
		await using var destination = new SqliteConnection(Unpooled(destinationPath));

		await source.OpenAsync(cancellationToken);
		await destination.OpenAsync(cancellationToken);
		source.BackupDatabase(destination);
		await StripConnectCredential(destination, cancellationToken);

		await using var check = destination.CreateCommand();
		check.CommandText = "PRAGMA integrity_check;";
		var result = (string?)await check.ExecuteScalarAsync(cancellationToken);

		if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException($"The database snapshot failed its integrity check: {result}.");
		}

		return destinationPath;
	}

	// The Macro Deck Connect credential is bound to the machine that signed in: it must never travel in an
	// archive, so it is deleted from the snapshot copy - never from the live database - before anything
	// can archive it. Unconditional by design; no setting may turn this off.
	private static async Task StripConnectCredential(SqliteConnection destination, CancellationToken ct)
	{
		await using var command = destination.CreateCommand();
		command.CommandText = """
							  DELETE FROM secret WHERE s_kind = $kind;
							  DELETE FROM app_preference WHERE ap_key LIKE 'connect.%';
							  """;
		command.Parameters.AddWithValue("$kind", (int)SecretKind.ConnectCredential);

		try
		{
			await command.ExecuteNonQueryAsync(ct);
		}
		catch (SqliteException)
		{
			// A snapshot taken before either table existed has nothing to strip.
		}
	}

	public string ReadSchemaVersion(string databasePath)
	{
		using var connection = new SqliteConnection(Unpooled(databasePath));
		connection.Open();

		using var command = connection.CreateCommand();
		command.CommandText =
			$"SELECT version FROM {DatabaseMigrationHelper.MetadataTable} ORDER BY id DESC LIMIT 1;";

		try
		{
			return command.ExecuteScalar() as string ?? string.Empty;
		}
		catch (SqliteException)
		{
			return string.Empty;
		}
	}

	// Pooling is off for every snapshot connection: a pooled connection keeps the sqlite3 file handle
	// open after it is disposed, and SQLite holds that handle for writing. On Windows the next opener
	// of the staged copy - the archive writer, which asks for FileShare.Read - then hits a sharing
	// violation, and the staging directory cannot be deleted either.
	private static string Unpooled(string databasePath)
		=> new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString();

	// A .tmp is a write the source host had not published yet. Startup recovery republishes anything
	// recoverable, so anything still named .tmp here is a write this installation never validated and
	// must not be carried into another one. Quarantined copies are damaged by definition.
	private static bool IsUnwantedSidecar(string relativePath)
		=> relativePath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
			relativePath.Contains(".corrupt-", StringComparison.OrdinalIgnoreCase);
}
