namespace MacroDeckHost.Application.Store.Model;

public sealed record StoreReleaseManifest
{
	public required string Version { get; init; }

	public required Uri ArtifactUrl { get; init; }

	public required string Sha256 { get; init; }

	public required long Size { get; init; }

	public DateTimeOffset? UploadedAt { get; init; }

	public StoreMediaAsset? Icon { get; init; }

	public IReadOnlyList<StoreMediaAsset> Screenshots { get; init; } = [];
}
