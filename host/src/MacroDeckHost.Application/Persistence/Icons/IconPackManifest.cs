using System.Text.Json.Serialization;
using MacroDeckHost.Application.Packaging;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Persistence.Icons;

public sealed class IconPackManifest
{
	public Guid Id { get; set; }

	public string Name { get; set; } = string.Empty;

	public string? Description { get; set; }

	public string? Author { get; set; }

	public string? Version { get; set; }

	public bool IsDefault { get; set; }

	public bool IsReadOnly { get; set; }

	public IconPackSourceType SourceType { get; set; } = IconPackSourceType.User;

	public string? SourceId { get; set; }

	public DateTime CreatedAt { get; set; }

	public DateTime UpdatedAt { get; set; }

	public List<IconManifestEntry> Icons { get; set; } = [];

	// IconPackManifest also backs internal pack persistence, and PersistenceJsonOptions.Default does not
	// ignore nulls globally, so these two must opt out individually to stay absent from unsigned packs.
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public List<PackageFileDigest>? Files { get; set; }

	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public PackageSignature? Signature { get; set; }
}
