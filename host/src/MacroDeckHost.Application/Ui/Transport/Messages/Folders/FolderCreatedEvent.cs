namespace MacroDeckHost.Application.Ui.Transport.Messages.Folders;

public class FolderCreatedEvent
{
	public Folder Folder { get; set; } = new();
}
