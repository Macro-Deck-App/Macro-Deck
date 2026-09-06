using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Icons;

public class IconPack
{
	public string Id { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string? Description { get; set; }
	public string? Author { get; set; }
	public string? Version { get; set; }
	public bool IsDefault { get; set; }
	public bool IsReadOnly { get; set; }
	public string SourceType { get; set; } = string.Empty;
	public DateTime CreatedAt { get; set; }
	public DateTime UpdatedAt { get; set; }
	public int IconCount { get; set; }
	public string OwnerKind { get; set; } = nameof(IconPackOwnerKind.User);
	public bool CanDelete { get; set; } = true;
}
