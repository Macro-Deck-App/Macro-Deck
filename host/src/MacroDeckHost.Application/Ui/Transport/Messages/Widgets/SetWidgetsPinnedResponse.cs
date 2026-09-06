namespace MacroDeckHost.Application.Ui.Transport.Messages.Widgets;

public class SetWidgetsPinnedResponse
{
	public bool Success { get; set; }
	public TransportError? Error { get; set; }
	public List<Widget>? Widgets { get; set; }
}
