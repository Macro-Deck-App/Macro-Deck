namespace MacroDeckHost.Application.Ui.Transport.Messages.Widgets;

public class UpdateWidgetStateResponse
{
	public bool Success { get; set; }
	public TransportError? Error { get; set; }
}
