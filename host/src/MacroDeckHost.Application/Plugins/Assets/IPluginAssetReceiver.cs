namespace MacroDeckHost.Application.Plugins.Assets;

public interface IPluginAssetReceiver
{
	event EventHandler<AssetCommittedEventArgs>? AssetCommitted;

	AssetOperationResult Begin(string pluginId,
		string assetId,
		string kind,
		string mimeType,
		int totalBytes,
		string contentHash);

	AssetOperationResult Chunk(string pluginId, string assetId, int index, ReadOnlySpan<byte> data);

	AssetOperationResult Commit(string pluginId, string assetId);

	void DropSession(string pluginId);
}

public sealed record AssetOperationResult(bool Accepted, string? ErrorCode = null, string? ErrorMessage = null)
{
	public static AssetOperationResult Ok() => new(true);

	public static AssetOperationResult Fail(string errorCode, string errorMessage) =>
		new(false, errorCode, errorMessage);
}

public sealed class AssetCommittedEventArgs(
	string pluginId,
	string kind,
	string contentHash,
	string mimeType,
	byte[] bytes)
	: EventArgs
{
	public string PluginId { get; } = pluginId;

	public string Kind { get; } = kind;

	public string ContentHash { get; } = contentHash;

	public string MimeType { get; } = mimeType;

	public byte[] Bytes { get; } = bytes;
}
