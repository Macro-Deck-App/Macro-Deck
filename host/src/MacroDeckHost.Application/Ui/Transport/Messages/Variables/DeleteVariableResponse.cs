namespace MacroDeckHost.Application.Ui.Transport.Messages.Variables;

public class DeleteVariableResponse
{
	public bool Success { get; set; }
	public TransportError? Error { get; set; }
}
