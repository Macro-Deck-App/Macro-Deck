using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Envelope;

namespace MacroDeckHost.Tests.PluginContractTests.Harness;

internal sealed class FakeAssetUploader : IPluginAssetUploader
{
	public List<(string Kind, string MimeType, byte[] Data)> Uploads { get; } = [];

	public Task<string> UploadAsync(string kind, string mimeType, byte[] data, CancellationToken cancellationToken)
	{
		Uploads.Add((kind, mimeType, data));
		return Task.FromResult(AssetContentHash.Compute(data));
	}

	public bool TryComplete(ProtocolEnvelope ack) => false;
}
