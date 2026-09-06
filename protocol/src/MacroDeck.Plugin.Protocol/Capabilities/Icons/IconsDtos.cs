namespace MacroDeck.Plugin.Protocol.Capabilities.Icons;

/// <summary>
/// The full result of the <c>icons</c> capability's <c>describe</c> operation - metadata only, never the
/// bytes. A capability response is a single message and an icon is chunked, so the bytes travel
/// separately over the <c>asset.*</c> pipeline, pushed by the plugin at connect - this payload just lets
/// the host know what to expect and verify: the mime type it should serve the bytes as, how many bytes
/// to expect, and the content hash the asset upload must match.
/// </summary>
public sealed record IconsDescribePayload
{
	public required string MimeType { get; init; }

	public required int ByteLength { get; init; }

	/// <summary>Must match the <c>contentHash</c> the plugin declares in the matching <c>asset.begin</c>
	/// for kind <c>icon</c> - see <c>MacroDeck.Plugin.Protocol.Assets.AssetContentHash</c>.</summary>
	public required string ContentHash { get; init; }
}
