using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Domain.Entities;

public class IconPackEntity : BaseEntity
{
	public required string Name { get; set; }

	public string? Description { get; set; }

	public string? Author { get; set; }

	public string? Version { get; set; }

	public bool IsDefault { get; set; }

	public bool IsReadOnly { get; set; }

	public IconPackSourceType SourceType { get; set; } = IconPackSourceType.User;

	public string? SourceId { get; set; }

	public DateTime UpdatedAt { get; set; }
}
