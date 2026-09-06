namespace MacroDeckHost.Application.Ui.Transport.Messages.Widgets;

public class WidgetsDeletedEvent
{
	public string FolderId { get; set; } = string.Empty;
	public List<string> WidgetIds { get; set; } = [];
}
