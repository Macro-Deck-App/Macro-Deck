namespace MacroDeckHost.Application.Ui.Transport.Messages.Widgets;

public class DeleteWidgetsResponse
{
	public bool Success { get; set; }
	public TransportError? Error { get; set; }
}
