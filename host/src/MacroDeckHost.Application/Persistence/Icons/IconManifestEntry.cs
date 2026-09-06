using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Persistence.Icons;

public sealed class IconManifestEntry
{
	public Guid Id { get; set; }

	public string Name { get; set; } = string.Empty;

	public int? Width { get; set; }

	public int? Height { get; set; }

	public bool IsAnimated { get; set; }

	public int? FrameCount { get; set; }

	public string? SourceContentHash { get; set; }

	public string? MasterContentHash { get; set; }

	public string? DeclaredSourceContentHash { get; set; }

	public string? Checksum { get; set; }

	public Guid? SourceIconId { get; set; }

	public string? OriginalFileName { get; set; }

	public string? OriginalFormat { get; set; }

	public IconProcessingState State { get; set; } = IconProcessingState.Pending;

	public string? ProcessingError { get; set; }

	public List<int> AvailableSizes { get; set; } = [];

	public Guid? ImportBatchId { get; set; }

	public DateTime CreatedAt { get; set; }

	public DateTime UpdatedAt { get; set; }
}
