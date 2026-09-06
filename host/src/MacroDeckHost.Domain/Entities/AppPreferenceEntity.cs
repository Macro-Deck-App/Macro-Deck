namespace MacroDeckHost.Domain.Entities;

public class AppPreferenceEntity
{
	public required string Key { get; set; }

	public string Value { get; set; } = string.Empty;

	public DateTime CreatedAt { get; set; }

	public DateTime UpdatedAt { get; set; }
}
