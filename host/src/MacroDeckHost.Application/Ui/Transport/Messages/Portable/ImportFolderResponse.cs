using MacroDeckHost.Application.Ui.Transport.Messages.Folders;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Portable;

public sealed class ImportFolderResponse
{
	public bool Success { get; set; }

	public TransportError? Error { get; set; }

	public Folder? Folder { get; set; }

	public int FolderCount { get; set; }
}
