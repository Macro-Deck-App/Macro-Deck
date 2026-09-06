namespace MacroDeckHost.Application.Ui.Transport.Messages.Widgets;

public class WidgetPositionsUpdatedEvent
{
	public string FolderId { get; set; } = string.Empty;
	public List<Widget> Widgets { get; set; } = [];
}
