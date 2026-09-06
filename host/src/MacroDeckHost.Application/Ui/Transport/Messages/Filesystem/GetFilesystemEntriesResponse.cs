namespace MacroDeckHost.Application.Ui.Transport.Messages.Filesystem;

public class GetFilesystemEntriesResponse
{
	public string Path { get; set; } = string.Empty;

	public string? ParentPath { get; set; }

	public List<FilesystemEntry> Entries { get; set; } = new();

	public TransportError? Error { get; set; }
}

public class FilesystemEntry
{
	public string Name { get; set; } = string.Empty;

	public string Path { get; set; } = string.Empty;

	public bool IsDirectory { get; set; }
}
