namespace MacroDeckHost.Integrations.AutoHotkey.State;

internal sealed record AutoHotkeyScript
{
	public required Guid Id { get; init; }

	public required string Name { get; init; }

	public string? Description { get; init; }

	public required string Slug { get; init; }

	public DateTimeOffset CreatedAt { get; init; }

	public DateTimeOffset UpdatedAt { get; init; }

	public bool StartWithMacroDeck { get; init; }

	public bool IncludeHelper { get; init; } = true;

	public string? WorkingDirectory { get; init; }

	public bool SourceUnreadable { get; init; }
}

internal sealed record AutoHotkeyScriptSettings(
	string? Description,
	bool StartWithMacroDeck,
	bool IncludeHelper,
	string? WorkingDirectory);

internal enum AutoHotkeySourceStatus
{
	Found,
	Missing,
	Unreadable
}

internal sealed record AutoHotkeySource(AutoHotkeySourceStatus Status, string Text)
{
	public bool IsWritable => Status != AutoHotkeySourceStatus.Unreadable;
}

internal enum AutoHotkeyLibraryChangeKind
{
	Loaded,
	Added,
	Renamed,
	Deleted,
	SettingsChanged,
	SourceSaved
}

internal sealed record AutoHotkeyLibraryChange(AutoHotkeyLibraryChangeKind Kind, Guid? ScriptId);

internal enum AutoHotkeyLibraryError
{
	ReadOnly,
	NotFound,
	InvalidName,
	SourceUnreadable,
	TooLarge,
	NotUtf8,
	ExportFailed
}

internal sealed class AutoHotkeyLibraryException : Exception
{
	public AutoHotkeyLibraryException(AutoHotkeyLibraryError error)
		: base($"AutoHotkey script library: {error}")
	{
		Error = error;
	}

	public AutoHotkeyLibraryException(AutoHotkeyLibraryError error, Exception inner)
		: base($"AutoHotkey script library: {error}", inner)
	{
		Error = error;
	}

	public AutoHotkeyLibraryError Error { get; }
}

internal enum AutoHotkeyExportStatus
{
	Exported,
	AlreadyExists
}

internal sealed record AutoHotkeyExportResult(AutoHotkeyExportStatus Status, string FilePath);
