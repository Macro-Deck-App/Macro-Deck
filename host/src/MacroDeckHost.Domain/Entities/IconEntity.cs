using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Domain.Entities;

public class IconEntity : BaseEntity
{
	public required Guid PackId { get; set; }

	public required string Name { get; set; }

	public int? Width { get; set; }

	public int? Height { get; set; }

	public bool IsAnimated { get; set; }

	public int? FrameCount { get; set; }

	// Locally computed and trusted for import deduplication.
	public string? SourceContentHash { get; set; }

	public string? MasterContentHash { get; set; }

	public string? DeclaredSourceContentHash { get; set; }

	// The icon's id in the source pack archive. Correlates an installed icon with the same icon in a
	// later version of that pack, so a store upgrade can keep the local id buttons reference.
	public Guid? SourceIconId { get; set; }

	public string? OriginalFileName { get; set; }

	public string? OriginalFormat { get; set; }

	public IconProcessingState ProcessingState { get; set; } = IconProcessingState.Pending;

	public string? ProcessingError { get; set; }

	public IReadOnlyList<int> AvailableSizes { get; set; } = [];

	public Guid? ImportBatchId { get; set; }

	public DateTime UpdatedAt { get; set; }
}
