namespace MacroDeckHost.Application.Ui.Transport.Messages.Folders;

public class FolderUpdatedEvent
{
	public Folder Folder { get; set; } = new();
}
