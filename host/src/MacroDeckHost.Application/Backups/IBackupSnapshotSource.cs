using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Backups;

public sealed record BackupSnapshotFile(string RelativePath, string AbsolutePath, BackupComponentGroup Component);

public sealed record BackupSnapshotPlan(
	IReadOnlyList<BackupSnapshotFile> Files,
	IReadOnlyList<BackupSkippedEntry> Skipped,
	long TotalBytes);

public interface IBackupSnapshotSource
{
	BackupSnapshotPlan Plan();

	/// <summary>
	/// Copies the live database through SQLite's online backup API, which takes a transactionally
	/// consistent copy of a database that is being written to, and verifies the copy before it is used.
	/// </summary>
	Task<string> CopyDatabase(string destinationPath, CancellationToken cancellationToken = default);

	string ReadSchemaVersion(string databasePath);
}
