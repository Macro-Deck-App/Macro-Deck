using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Persistence.Icons;

public sealed class IconImportBatchFile
{
	public Guid Id { get; set; }

	public Guid PackId { get; set; }

	public string? SourceName { get; set; }

	public IconImportBatchState State { get; set; } = IconImportBatchState.Discovering;

	public int? Total { get; set; }

	public string? Error { get; set; }

	public int Skipped { get; set; }

	public bool ExplicitDestination { get; set; }

	public IconImportMode Mode { get; set; }

	public bool Silent { get; set; }

	public DateTime CreatedAt { get; set; }

	public DateTime UpdatedAt { get; set; }
}
