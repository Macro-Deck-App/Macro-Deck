namespace MacroDeckHost.Application.Persistence.Automations;

public sealed class AutomationFile
{
	public Guid Id { get; set; }

	public string Name { get; set; } = string.Empty;

	public string Description { get; set; } = string.Empty;

	public bool Enabled { get; set; } = true;

	public string Flows { get; set; } = string.Empty;

	public DateTime CreatedAt { get; set; }

	public DateTime UpdatedAt { get; set; }
}
