namespace MacroDeckHost.Application.Ui.Transport.Messages.Widgets;

public class LabelTextUpdatedEvent
{
	public string WidgetId { get; set; } = string.Empty;
	public string State { get; set; } = "off";
	public string? Text { get; set; }
}
