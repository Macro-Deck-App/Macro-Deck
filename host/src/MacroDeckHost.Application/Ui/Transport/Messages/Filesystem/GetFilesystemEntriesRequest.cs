namespace MacroDeckHost.Application.Ui.Transport.Messages.Filesystem;

public class GetFilesystemEntriesRequest
{
	public string? Path { get; set; }

	public List<string>? Extensions { get; set; }

	public bool DirectoriesOnly { get; set; }
}
