using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Backups;

/// <summary>
/// The restore intent written once staging is complete and validated. It is the only thing the boot-time
/// applier reads, so it has to be self-contained: the applier runs before dependency injection exists.
/// </summary>
public sealed class PendingRestoreDocument
{
	public const string FileName = "pending-restore.json";

	public Guid RestoreId { get; set; }

	public Guid BackupId { get; set; }

	public DateTimeOffset StagedAt { get; set; }

	public List<BackupComponentGroup> Components { get; set; } = [];

	public string StagingDirectory { get; set; } = string.Empty;

	public string ApplyDirectory { get; set; } = string.Empty;

	public string? DatabasePath { get; set; }

	public List<PendingRestoreFile> Files { get; set; } = [];

	public List<string> Tables { get; set; } = [];
}

public sealed class PendingRestoreFile
{
	public string RelativePath { get; set; } = string.Empty;

	public string Sha256 { get; set; } = string.Empty;

	public BackupComponentGroup Component { get; set; }
}

public sealed class RestoreJournalEntry
{
	public string RelativePath { get; set; } = string.Empty;

	public bool MovedAside { get; set; }

	public bool Applied { get; set; }
}
