namespace MacroDeck.Plugin.Protocol.Assets;

/// <summary>
/// Payload of <c>asset.begin</c>, starting a chunked asset upload. <c>TotalBytes</c> is checked
/// against <see cref="MacroDeck.Plugin.Protocol.Limits.ProtocolLimits.MaxAssetBytes" /> up front, so
/// an oversize upload is rejected before the first chunk rather than discovered mid-stream.
/// </summary>
public sealed record AssetBeginPayload
{
	/// <summary>Identifies this upload; referenced by every subsequent <c>asset.chunk</c> and
	/// <c>asset.commit</c> for it.</summary>
	public required string AssetId { get; init; }

	/// <summary>One of <see cref="AssetKinds" />.</summary>
	public required string Kind { get; init; }

	public required string MimeType { get; init; }

	public required int TotalBytes { get; init; }

	/// <summary>Content hash of the complete asset, checked on commit.</summary>
	public required string ContentHash { get; init; }
}
