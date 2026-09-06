namespace MacroDeckHost.Domain.Entities;

public class IntegrationConfigEntryEntity : BaseEntity
{
	public required string IntegrationId { get; set; }

	public string Title { get; set; } = string.Empty;

	public string ValuesJson { get; set; } = "{}";

	public DateTime UpdatedAt { get; set; }
}
