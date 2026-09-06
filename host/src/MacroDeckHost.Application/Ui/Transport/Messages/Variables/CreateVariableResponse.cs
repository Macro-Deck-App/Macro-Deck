namespace MacroDeckHost.Application.Ui.Transport.Messages.Variables;

public class CreateVariableResponse
{
	public bool Success { get; set; }
	public TransportError? Error { get; set; }
	public Variable? Variable { get; set; }
}
