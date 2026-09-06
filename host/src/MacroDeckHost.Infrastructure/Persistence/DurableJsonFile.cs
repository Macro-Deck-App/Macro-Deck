using System.Globalization;
using System.Text.Json;
using MacroDeckHost.Application.Persistence;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Persistence;

internal enum PersistenceBackup
{
	None,
	KeepLastKnownGood,
}

internal sealed class DurableJsonFile
{
	public const string TempSuffix = ".tmp";
	public const string BackupSuffix = ".bak";
	public const string CorruptSuffix = ".corrupt-";

	private const int MaxPreservedCorruptCopies = 3;
	private static readonly byte[] _utf8Bom = [0xEF, 0xBB, 0xBF];

	private readonly string _dataKind;
	private readonly JsonSerializerOptions _options;
	private readonly ILogger _logger;
	private readonly PersistenceBackup _backup;
	private readonly IPersistenceRecoveryReporter? _recoveryReporter;

	public DurableJsonFile(
		string dataKind,
		JsonSerializerOptions options,
		ILogger logger,
		PersistenceBackup backup = PersistenceBackup.None,
		IPersistenceRecoveryReporter? recoveryReporter = null)
	{
		_dataKind = dataKind;
		_options = options;
		_logger = logger;
		_backup = backup;
		_recoveryReporter = recoveryReporter;
	}

	public void Write<T>(string path, T value)
		=> WriteBytes(path, JsonSerializer.SerializeToUtf8Bytes(value, _options), rotateBackup: true);

	public T? Read<T>(string path, bool repair = true)
		where T : class
	{
		try
		{
			return Recover<T>(path, repair);
		}
		catch (Exception ex) when (ex is IOException
			or UnauthorizedAccessException
			or JsonException
			or NotSupportedException)
		{
			_logger.Error(ex, "Failed to recover {DataKind} from {Path}", _dataKind, path);
			return null;
		}
	}

	public void Delete(string path)
	{
		TryDelete(path);
		TryDelete(path + TempSuffix);
		TryDelete(path + BackupSuffix);
	}

	public static IEnumerable<string> EnumerateDocumentPaths(string directory, string extension)
	{
		if (!Directory.Exists(directory))
		{
			yield break;
		}

		var seen = new HashSet<string>(StringComparer.Ordinal);
		foreach (var path in Directory.EnumerateFiles(directory, "*" + extension))
		{
			if (path.EndsWith(extension, StringComparison.Ordinal) && seen.Add(path))
			{
				yield return path;
			}
		}

		// A crash between the backup and publish renames leaves no primary at all, so a document is
		// also present when only its backup survives. A lone temporary file is deliberately not a
		// document: older versions abandoned one on every failed save, and treating those as
		// documents would resurrect records the user deleted (issue #647).
		var backupExtension = extension + BackupSuffix;
		foreach (var path in Directory.EnumerateFiles(directory, "*" + backupExtension))
		{
			if (!path.EndsWith(backupExtension, StringComparison.Ordinal))
			{
				continue;
			}

			var primary = path[..^BackupSuffix.Length];
			if (seen.Add(primary))
			{
				yield return primary;
			}
		}
	}

	private void WriteBytes(string path, byte[] bytes, bool rotateBackup)
	{
		var directory = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(directory))
		{
			Directory.CreateDirectory(directory);
		}

		var tempPath = path + TempSuffix;
		using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
		{
			stream.Write(bytes, 0, bytes.Length);

			// The bytes must reach physical storage before the rename publishes them. Without this a
			// power loss can leave a renamed but empty or truncated file (issue #647).
			stream.Flush(flushToDisk: true);
		}

		try
		{
			if (rotateBackup && _backup == PersistenceBackup.KeepLastKnownGood && File.Exists(path))
			{
				File.Move(path, path + BackupSuffix, overwrite: true);
			}

			File.Move(tempPath, path, overwrite: true);
		}
		catch
		{
			// The caller is about to be told this save failed, so the temporary file must not survive
			// as a recovery candidate and resurrect it on the next start. A temporary file that does
			// outlive a write therefore always means the process died mid-write.
			TryDelete(tempPath);
			throw;
		}
	}

	private T? Recover<T>(string path, bool repair)
		where T : class
	{
		var tempPath = path + TempSuffix;
		var backupPath = path + BackupSuffix;

		var primary = TryLoad<T>(path);
		if (primary is not null)
		{
			// A temporary file that outlived a successful primary is only newer when the write was
			// flushed but never published; anything older is an abandoned write.
			var pending = TryLoad<T>(tempPath);
			if (pending is not null && IsNewerThan(tempPath, path))
			{
				return repair
					? Publish(path,
						pending,
						PersistenceRecoverySource.PendingWrite,
						preservedCorruptPath: null,
						rotateBackup: true)
					: pending.Value;
			}

			if (repair)
			{
				TryDelete(tempPath);
			}

			return primary.Value;
		}

		var primaryExists = File.Exists(path);
		var candidate = TryLoad<T>(tempPath);
		var source = PersistenceRecoverySource.PendingWrite;
		if (candidate is null)
		{
			candidate = TryLoad<T>(backupPath);
			source = PersistenceRecoverySource.Backup;
		}

		if (candidate is null)
		{
			if (primaryExists)
			{
				_logger.Error(
					"Failed to read {DataKind} from {Path} and no valid backup or pending write was available; " +
					"leaving the file untouched",
					_dataKind,
					path);
				_recoveryReporter?.ReportUnrecoverable(new PersistenceLoss(_dataKind, path));
			}

			return null;
		}

		if (!repair)
		{
			return candidate.Value;
		}

		// The damaged primary is moved aside before anything is published, so the publish can never
		// rotate corrupt content into the backup that is being recovered from. If it cannot be moved
		// aside, nothing is published at all: publishing would rotate the corrupt primary into the
		// backup and destroy the copy this recovery just came from.
		var preservedCorruptPath = primaryExists ? PreserveCorrupt(path) : null;
		if (primaryExists && preservedCorruptPath is null)
		{
			_logger.Error("Recovered {DataKind} at {Path} in memory only; the damaged file could not be moved aside, " +
				"so every file was left untouched",
				_dataKind,
				path);
			_recoveryReporter?.ReportRecovered(new PersistenceRecovery(_dataKind,
				path,
				source,
				PreservedCorruptPath: null));
			return candidate.Value;
		}

		// Whatever occupied the primary slot was corrupt or absent, so it must never be rotated into
		// the backup this recovery may have just read from.
		return Publish(path, candidate, source, preservedCorruptPath, rotateBackup: false);
	}

	private T Publish<T>(
		string path,
		Loaded<T> candidate,
		PersistenceRecoverySource source,
		string? preservedCorruptPath,
		bool rotateBackup)
		where T : class
	{
		try
		{
			// The candidate's own bytes are republished rather than a re-serialization of the parsed
			// model, so a legacy-format document keeps every member the current type does not model.
			WriteBytes(path, candidate.Bytes, rotateBackup);
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Recovered {DataKind} from {Path} but could not republish it", _dataKind, path);
			return candidate.Value;
		}

		_logger.Warning("Recovered {DataKind} at {Path} from its {Source} after an interrupted write; the damaged " +
			"file was preserved as {PreservedPath}",
			_dataKind,
			path,
			source,
			preservedCorruptPath ?? "<none>");
		_recoveryReporter?.ReportRecovered(new PersistenceRecovery(_dataKind, path, source, preservedCorruptPath));
		return candidate.Value;
	}

	private string? PreserveCorrupt(string path)
	{
		try
		{
			var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
			var corruptPath = path + CorruptSuffix + stamp;
			var attempt = 1;
			while (File.Exists(corruptPath))
			{
				corruptPath = path + CorruptSuffix + stamp + "-" + attempt++;
			}

			File.Move(path, corruptPath);
			PruneCorruptCopies(path);
			return corruptPath;
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Failed to preserve the damaged {DataKind} file at {Path}", _dataKind, path);
			return null;
		}
	}

	private void PruneCorruptCopies(string path)
	{
		var directory = Path.GetDirectoryName(path);
		if (string.IsNullOrEmpty(directory))
		{
			return;
		}

		var prefix = Path.GetFileName(path) + CorruptSuffix;
		var existing = Directory
			.EnumerateFiles(directory, prefix + "*")
			.Where(candidate => Path.GetFileName(candidate).StartsWith(prefix, StringComparison.Ordinal))
			.OrderByDescending(candidate => candidate, StringComparer.Ordinal)
			.Skip(MaxPreservedCorruptCopies);

		foreach (var stale in existing)
		{
			TryDelete(stale);
		}
	}

	private Loaded<T>? TryLoad<T>(string path)
		where T : class
	{
		try
		{
			if (!File.Exists(path))
			{
				return null;
			}

			var bytes = File.ReadAllBytes(path);
			if (bytes.Length == 0)
			{
				return null;
			}

			var content = bytes.AsSpan();
			if (content.StartsWith(_utf8Bom))
			{
				content = content[_utf8Bom.Length..];
			}

			var value = JsonSerializer.Deserialize<T>(content, _options);
			return value is null ? null : new Loaded<T>(value, bytes);
		}
		catch (Exception ex) when (ex is JsonException
			or IOException
			or UnauthorizedAccessException
			or NotSupportedException)
		{
			_logger.Warning(ex, "Failed to read {DataKind} from {Path}", _dataKind, path);
			return null;
		}
	}

	private static bool IsNewerThan(string path, string other)
	{
		try
		{
			return File.GetLastWriteTimeUtc(path) > File.GetLastWriteTimeUtc(other);
		}
		catch (IOException)
		{
			return false;
		}
	}

	private void TryDelete(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			_logger.Warning(ex, "Failed to delete {DataKind} file {Path}", _dataKind, path);
		}
	}

	private sealed record Loaded<T>(T Value, byte[] Bytes)
		where T : class;
}
