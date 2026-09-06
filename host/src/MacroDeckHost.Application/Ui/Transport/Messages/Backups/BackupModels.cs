namespace MacroDeckHost.Application.Ui.Transport.Messages.Backups;

public sealed class BackupSummary
{
	public Guid Id { get; set; }

	public string ProviderId { get; set; } = string.Empty;

	public string ProviderDisplayName { get; set; } = string.Empty;

	public string Name { get; set; } = string.Empty;

	public DateTimeOffset CreatedAt { get; set; }

	public string Trigger { get; set; } = string.Empty;

	public string MacroDeckVersion { get; set; } = string.Empty;

	public long SizeBytes { get; set; }

	public int FormatVersion { get; set; }

	public bool IsRemote { get; set; }

	public bool DecryptableLocally { get; set; }

	public bool Imported { get; set; }

	public string? Note { get; set; }

	public List<string> Components { get; set; } = [];
}

public sealed class PendingRestoreSummary
{
	public Guid RestoreId { get; set; }

	public Guid BackupId { get; set; }

	public DateTimeOffset StagedAt { get; set; }

	public List<string> Components { get; set; } = [];

	/// <summary>
	/// Whether the host can restart itself to apply this restore. Reported up front so the pane can say
	/// what has to happen instead of offering a button that only fails once it is pressed.
	/// </summary>
	public bool RestartSupported { get; set; }

	public string? RestartUnavailableReason { get; set; }
}

public sealed class BackupComponentCatalogEntry
{
	public string Id { get; set; } = string.Empty;

	public List<string> Requires { get; set; } = [];
}

public sealed class BackupComponentGroupInfoDto
{
	public string Id { get; set; } = string.Empty;

	public int EntryCount { get; set; }

	public long ByteSize { get; set; }

	public List<string> Requires { get; set; } = [];
}

public sealed class BackupDependencyWarningDto
{
	public string Group { get; set; } = string.Empty;

	public string MissingDependency { get; set; } = string.Empty;
}
