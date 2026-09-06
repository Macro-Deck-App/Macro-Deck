namespace MacroDeckHost.Domain.Entities;

public class AutomationEntity : BaseEntity
{
	public required string Name { get; set; }

	public string Description { get; set; } = string.Empty;

	public bool Enabled { get; set; } = true;

	public string Flows { get; set; } = string.Empty;

	public DateTime UpdatedAt { get; set; }
}
