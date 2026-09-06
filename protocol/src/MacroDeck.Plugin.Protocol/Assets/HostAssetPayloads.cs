namespace MacroDeck.Plugin.Protocol.Assets;

/// <summary>
/// Payload of <c>host.asset.begin</c>, starting a chunked host-to-plugin asset upload - the same shape
/// as <see cref="AssetBeginPayload" />, mirrored for the opposite direction. <c>TotalBytes</c> is checked
/// against <see cref="MacroDeck.Plugin.Protocol.Limits.ProtocolLimits.MaxAssetBytes" /> up front, exactly
/// as the plugin-to-host pipeline does.
/// </summary>
public sealed record HostAssetBeginPayload
{
	/// <summary>Identifies this upload; referenced by every subsequent <c>host.asset.chunk</c> and
	/// <c>host.asset.commit</c> for it.</summary>
	public required string AssetId { get; init; }

	/// <summary>One of <see cref="AssetKinds" />.</summary>
	public required string Kind { get; init; }

	public required string MimeType { get; init; }

	public required int TotalBytes { get; init; }

	/// <summary>Content hash of the complete asset, checked on commit. See <see cref="AssetContentHash" />.</summary>
	public required string ContentHash { get; init; }
}

/// <summary>
/// Payload of <c>host.asset.chunk</c>, one chunk of an upload started by <c>host.asset.begin</c>. Carries
/// an explicit <see cref="Index" /> for the same reason <see cref="AssetChunkPayload" /> does.
/// <see cref="Data" /> is base64 and bounded by
/// <see cref="MacroDeck.Plugin.Protocol.Limits.ProtocolLimits.MaxAssetChunkBytes" /> pre-encoding.
/// </summary>
public sealed record HostAssetChunkPayload
{
	public required string AssetId { get; init; }

	public required int Index { get; init; }

	/// <summary>Base64-encoded chunk bytes.</summary>
	public required string Data { get; init; }
}

/// <summary>Payload of <c>host.asset.commit</c>, finalising the upload started by <c>host.asset.begin</c>.
/// The receiver verifies total size and content hash against what <c>host.asset.begin</c> declared before
/// treating the asset as complete.</summary>
public sealed record HostAssetCommitPayload
{
	public required string AssetId { get; init; }
}

/// <summary>Payload of <c>host.asset.ack</c>, acknowledging a <c>host.asset.begin</c>, <c>host.asset.chunk</c>
/// or <c>host.asset.commit</c>. Exempt from backpressure, like every reply type.</summary>
public sealed record HostAssetAckPayload
{
	public required string AssetId { get; init; }

	/// <summary>The chunk index being acknowledged. Absent when acknowledging <c>host.asset.begin</c> or
	/// <c>host.asset.commit</c>, which carry no index of their own.</summary>
	public int? Index { get; init; }

	public required bool Accepted { get; init; }
}
