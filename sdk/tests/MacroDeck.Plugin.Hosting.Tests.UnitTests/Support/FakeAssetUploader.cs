using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Envelope;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;

/// <summary>A no-transport <see cref="IPluginAssetUploader" /> for handler-level tests that only need
/// the artwork/icon upload seam to be satisfiable, not the wire exchange behind it - see
/// <see cref="MacroDeck.Plugin.Hosting.Tests.UnitTests.PluginAssetUploaderTests" /> for the real thing.</summary>
internal sealed class FakeAssetUploader : IPluginAssetUploader
{
	public List<(string Kind, string MimeType, byte[] Data)> Uploads { get; } = [];

	public Func<string, string, byte[], string>? ContentHashFor { get; set; }

	public Task<string> UploadAsync(string kind, string mimeType, byte[] data, CancellationToken cancellationToken)
	{
		Uploads.Add((kind, mimeType, data));
		var hash = ContentHashFor?.Invoke(kind, mimeType, data) ?? AssetContentHash.Compute(data);
		return Task.FromResult(hash);
	}

	public bool TryComplete(ProtocolEnvelope ack) => false;
}
