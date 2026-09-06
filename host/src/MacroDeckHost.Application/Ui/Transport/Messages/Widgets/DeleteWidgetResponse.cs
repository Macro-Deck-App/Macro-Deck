namespace MacroDeckHost.Application.Ui.Transport.Messages.Widgets;

public class DeleteWidgetResponse
{
	public bool Success { get; set; }
	public TransportError? Error { get; set; }
}
