namespace MacroDeckHost.Application.Ui.Transport.Messages.Automations;

public class CreateAutomationRequest
{
	public string Name { get; set; } = string.Empty;

	public string? Description { get; set; }

	public string? Flows { get; set; }
}
