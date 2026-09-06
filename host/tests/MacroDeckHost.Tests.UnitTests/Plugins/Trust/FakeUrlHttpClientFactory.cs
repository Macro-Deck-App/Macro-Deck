using MacroDeckHost.Tests.UnitTests.Http;

namespace MacroDeckHost.Tests.UnitTests.Plugins.Trust;

/// <summary>Serves <see cref="Body" /> for every request, so a <see cref="MacroDeck.Plugin.Packaging.Artifacts.PluginArtifactSourceKind.Url" />
/// source can be exercised without a real network call.</summary>
internal sealed class FakeUrlHttpClientFactory : IHttpClientFactory
{
	public byte[] Body { get; set; } = [];

	public HttpClient CreateClient(string name) => new(new FakeHttpMessageHandler(Body));
}
