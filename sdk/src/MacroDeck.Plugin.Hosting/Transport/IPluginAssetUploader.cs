using MacroDeck.Plugin.Protocol.Envelope;

namespace MacroDeck.Plugin.Hosting.Transport;

/// <summary>
/// Drives one <c>asset.*</c> upload: <c>asset.begin</c>, then one <c>asset.chunk</c> per
/// <see cref="Protocol.Limits.ProtocolLimits.MaxAssetChunkBytes" />-sized slice, then <c>asset.commit</c>,
/// waiting for the host's <c>asset.ack</c> after each step.
/// </summary>
internal interface IPluginAssetUploader
{
	/// <summary>
	/// Uploads <paramref name="data" /> as an asset of <paramref name="kind" /> (one of
	/// <see cref="Protocol.Assets.AssetKinds" />) and returns the content hash the host now has it
	/// cached under. Bounded overall by <see cref="Protocol.Limits.ProtocolTimeouts.AssetUpload" />;
	/// throws <see cref="AssetUploadException" /> when the host rejects any step, the connection drops,
	/// or the budget elapses.
	/// </summary>
	Task<string> UploadAsync(string kind, string mimeType, byte[] data, CancellationToken cancellationToken);

	/// <summary>Completes a pending upload step from the connection's receive loop. False when the
	/// correlation is unknown to this uploader.</summary>
	bool TryComplete(ProtocolEnvelope ack);
}

/// <summary>Thrown when an <c>asset.*</c> upload cannot complete - the host rejected a step, the
/// connection dropped mid-upload, or the overall budget elapsed.</summary>
internal sealed class AssetUploadException(string message, Exception? innerException = null)
	: Exception(message, innerException);
