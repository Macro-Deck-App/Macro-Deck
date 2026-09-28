namespace MacroDeckHost.Application.Ui.Transport.Messages.Variables;

public class SetVariableSharedResponse
{
	public bool Success { get; set; }
	public TransportError? Error { get; set; }
	public Variable? Variable { get; set; }
}
