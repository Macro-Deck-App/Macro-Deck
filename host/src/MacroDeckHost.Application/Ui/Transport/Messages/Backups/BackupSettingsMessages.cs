namespace MacroDeckHost.Application.Ui.Transport.Messages.Backups;

public sealed class GetBackupSettingsRequest;

public sealed class GetBackupSettingsResponse
{
	public string ScheduleFrequency { get; set; } = string.Empty;

	public string ScheduleTimeOfDay { get; set; } = string.Empty;

	public string ScheduleDayOfWeek { get; set; } = string.Empty;

	public int ScheduleDayOfMonth { get; set; }

	public string RetentionPolicy { get; set; } = string.Empty;

	public int RetentionKeepLatest { get; set; }

	public bool BeforeHostUpdate { get; set; }

	public bool BeforePluginUpdate { get; set; }

	public bool PreUpdateBackupSupported { get; set; }

	public string? PreUpdateBackupUnsupportedReason { get; set; }

	public int MinimumRetentionCount { get; set; }

	public int MaximumRetentionCount { get; set; }

	public DateTimeOffset? NextScheduledAt { get; set; }
}

public sealed class UpdateBackupSettingsRequest
{
	public string? ScheduleFrequency { get; set; }

	public string? ScheduleTimeOfDay { get; set; }

	public string? ScheduleDayOfWeek { get; set; }

	public int? ScheduleDayOfMonth { get; set; }

	public string? RetentionPolicy { get; set; }

	public int? RetentionKeepLatest { get; set; }

	public bool? BeforeHostUpdate { get; set; }

	public bool? BeforePluginUpdate { get; set; }
}

public sealed class UpdateBackupSettingsResponse
{
	public string ScheduleFrequency { get; set; } = string.Empty;

	public string ScheduleTimeOfDay { get; set; } = string.Empty;

	public string ScheduleDayOfWeek { get; set; } = string.Empty;

	public int ScheduleDayOfMonth { get; set; }

	public string RetentionPolicy { get; set; } = string.Empty;

	public int RetentionKeepLatest { get; set; }

	public bool BeforeHostUpdate { get; set; }

	public bool BeforePluginUpdate { get; set; }

	public bool PreUpdateBackupSupported { get; set; }

	public string? PreUpdateBackupUnsupportedReason { get; set; }

	public int MinimumRetentionCount { get; set; }

	public int MaximumRetentionCount { get; set; }

	public DateTimeOffset? NextScheduledAt { get; set; }
}
