namespace MacroDeckHost.Application.Ui.Transport.Messages.Widgets;

public class WidgetCreatedEvent
{
	public string FolderId { get; set; } = string.Empty;
	public Widget Widget { get; set; } = new();
}
