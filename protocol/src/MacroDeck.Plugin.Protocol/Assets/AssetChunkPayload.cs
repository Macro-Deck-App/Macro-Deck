namespace MacroDeck.Plugin.Protocol.Assets;

/// <summary>
/// Payload of <c>asset.chunk</c>, one chunk of an upload started by <c>asset.begin</c>. Carries an
/// explicit <see cref="Index" /> because ordering cannot be inherited from a transport that is
/// at-most-once with no cross-resume ordering guarantee. <see cref="Data" /> is base64 and bounded
/// by <see cref="MacroDeck.Plugin.Protocol.Limits.ProtocolLimits.MaxAssetChunkBytes" /> pre-encoding.
/// </summary>
public sealed record AssetChunkPayload
{
	public required string AssetId { get; init; }

	public required int Index { get; init; }

	/// <summary>Base64-encoded chunk bytes.</summary>
	public required string Data { get; init; }
}
