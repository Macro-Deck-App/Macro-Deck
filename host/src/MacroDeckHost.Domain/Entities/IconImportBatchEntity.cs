using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Domain.Entities;

public class IconImportBatchEntity : BaseEntity
{
	public required Guid PackId { get; set; }

	public string? SourceName { get; set; }

	public IconImportBatchState State { get; set; } = IconImportBatchState.Discovering;

	public int? Total { get; set; }

	public string? Error { get; set; }

	public int Skipped { get; set; }

	public bool ExplicitDestination { get; set; }

	public IconImportMode Mode { get; set; }

	public bool Silent { get; set; }

	public DateTime UpdatedAt { get; set; }
}
