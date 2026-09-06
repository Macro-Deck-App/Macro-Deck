namespace MacroDeckHost.Application.Plugins.Assets;

public interface IPluginAssetCache
{
	void Write(string contentHash, string mimeType, byte[] bytes);

	bool TryRead(string contentHash, out byte[] bytes, out string mimeType);
}
