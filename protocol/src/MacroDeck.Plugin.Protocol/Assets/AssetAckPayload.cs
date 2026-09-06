namespace MacroDeck.Plugin.Protocol.Assets;

/// <summary>Payload of <c>asset.ack</c>, acknowledging an <c>asset.begin</c>, <c>asset.chunk</c> or
/// <c>asset.commit</c>. Exempt from backpressure, like every reply type - it drains the sender's
/// queue rather than growing it.</summary>
public sealed record AssetAckPayload
{
	public required string AssetId { get; init; }

	/// <summary>The chunk index being acknowledged. Absent when acknowledging <c>asset.begin</c> or
	/// <c>asset.commit</c>, which carry no index of their own.</summary>
	public int? Index { get; init; }

	public required bool Accepted { get; init; }
}
