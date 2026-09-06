namespace MacroDeckHost.Application.Ui.Transport.Messages.Variables;

public class UpdateVariableResponse
{
	public bool Success { get; set; }
	public TransportError? Error { get; set; }
	public Variable? Variable { get; set; }
}
