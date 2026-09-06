namespace MacroDeckHost.Application.Ui.Transport.Messages.Automations;

public class UpdateAutomationRequest
{
	public string Id { get; set; } = string.Empty;

	public string? Name { get; set; }

	public string? Description { get; set; }

	public string? Flows { get; set; }

	public bool? Enabled { get; set; }
}
