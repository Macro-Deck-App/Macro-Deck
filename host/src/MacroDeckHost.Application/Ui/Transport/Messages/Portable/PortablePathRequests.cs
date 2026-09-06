namespace MacroDeckHost.Application.Ui.Transport.Messages.Portable;

public sealed class InspectArchivePathRequest
{
	public string Path { get; set; } = string.Empty;
}

public sealed class ImportProfileFromPathRequest
{
	public string Path { get; set; } = string.Empty;

	public string? Password { get; set; }
}

public sealed class ImportFolderFromPathRequest
{
	public string ProfileId { get; set; } = string.Empty;

	public string? ParentFolderId { get; set; }

	public string Path { get; set; } = string.Empty;

	public string? Password { get; set; }
}

public sealed class ImportWidgetsFromPathRequest
{
	public string FolderId { get; set; } = string.Empty;

	public int AnchorX { get; set; }

	public int AnchorY { get; set; }

	public string Path { get; set; } = string.Empty;

	public string? Password { get; set; }
}
