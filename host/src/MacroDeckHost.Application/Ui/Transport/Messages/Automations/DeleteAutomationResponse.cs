namespace MacroDeckHost.Application.Ui.Transport.Messages.Automations;

public class DeleteAutomationResponse
{
	public bool Success { get; set; }

	public TransportError? Error { get; set; }
}
