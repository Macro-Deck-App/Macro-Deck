namespace MacroDeckHost.Application.Ui.Transport.Messages.Automations;

public class CreateAutomationResponse
{
	public bool Success { get; set; }

	public Automation? Automation { get; set; }

	public TransportError? Error { get; set; }
}
