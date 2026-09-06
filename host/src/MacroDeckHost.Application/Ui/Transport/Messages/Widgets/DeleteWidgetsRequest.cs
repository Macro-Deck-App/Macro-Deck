namespace MacroDeckHost.Application.Ui.Transport.Messages.Widgets;

public class DeleteWidgetsRequest
{
	public string FolderId { get; set; } = string.Empty;

	public List<string> Ids { get; set; } = [];
}
