namespace MacroDeckHost.Application.Ui.Transport.Messages.Folders;

public class MoveFolderRequest
{
	public string Id { get; set; } = string.Empty;
	public string TargetId { get; set; } = string.Empty;
	public string Position { get; set; } = string.Empty;
}
