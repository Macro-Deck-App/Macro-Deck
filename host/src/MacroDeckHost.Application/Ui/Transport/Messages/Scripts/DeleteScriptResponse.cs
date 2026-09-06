namespace MacroDeckHost.Application.Ui.Transport.Messages.Scripts;

public class DeleteScriptResponse
{
	public bool Success { get; set; }

	public TransportError? Error { get; set; }
}
