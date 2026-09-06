namespace MacroDeck.Plugin.Protocol.Assets;

/// <summary>Payload of <c>asset.commit</c>, finalising the upload started by <c>asset.begin</c>. The
/// receiver verifies total size and content hash against what <c>asset.begin</c> declared before
/// treating the asset as complete.</summary>
public sealed record AssetCommitPayload
{
	public required string AssetId { get; init; }
}
