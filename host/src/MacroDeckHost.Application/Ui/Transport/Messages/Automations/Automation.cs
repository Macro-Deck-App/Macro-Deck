namespace MacroDeckHost.Application.Ui.Transport.Messages.Automations;

public class Automation
{
	public string Id { get; set; } = string.Empty;

	public string Name { get; set; } = string.Empty;

	public string Description { get; set; } = string.Empty;

	public bool Enabled { get; set; }

	public string Flows { get; set; } = string.Empty;

	public DateTime CreatedAt { get; set; }

	public DateTime UpdatedAt { get; set; }
}
