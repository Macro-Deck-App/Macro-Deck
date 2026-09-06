using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Backups;

public sealed class BackupContentIndex
{
	public List<BackupContentEntry> Entries { get; set; } = [];

	public List<BackupComponentEntry> Components { get; set; } = [];

	public BackupDatabaseInfo? Database { get; set; }

	public List<BackupSkippedEntry> Skipped { get; set; } = [];
}

public sealed class BackupContentEntry
{
	public string Path { get; set; } = string.Empty;

	public long Size { get; set; }

	public string Sha256 { get; set; } = string.Empty;

	public BackupComponentGroup Component { get; set; }
}

public sealed class BackupComponentEntry
{
	public BackupComponentGroup Id { get; set; }

	public int EntryCount { get; set; }

	public long ByteSize { get; set; }

	public List<string> Tables { get; set; } = [];
}

public sealed class BackupDatabaseInfo
{
	public string Path { get; set; } = BackupFileNames.DatabaseEntry;

	public long Size { get; set; }

	public string Sha256 { get; set; } = string.Empty;

	public string SchemaVersion { get; set; } = string.Empty;
}

public sealed class BackupSkippedEntry
{
	public string Path { get; set; } = string.Empty;

	public string Reason { get; set; } = string.Empty;
}
