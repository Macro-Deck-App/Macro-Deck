using System.Collections.Concurrent;
using MacroDeckHost.Application.Plugins.Assets;

namespace MacroDeckHost.Tests.PluginContractTests.Harness;

internal sealed class InMemoryPluginAssetCache : IPluginAssetCache
{
	private readonly ConcurrentDictionary<string, (byte[] Bytes, string MimeType)>
		_byHash = new(StringComparer.Ordinal);

	public void Write(string contentHash, string mimeType, byte[] bytes) => _byHash[contentHash] = (bytes, mimeType);

	public bool TryRead(string contentHash, out byte[] bytes, out string mimeType)
	{
		if (_byHash.TryGetValue(contentHash, out var cached))
		{
			bytes = cached.Bytes;
			mimeType = cached.MimeType;
			return true;
		}

		bytes = [];
		mimeType = string.Empty;
		return false;
	}
}
