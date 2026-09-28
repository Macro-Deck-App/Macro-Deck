namespace MacroDeck.Sdk.VideoStreams;

/// <summary>The host's identity for a registered video stream provider.</summary>
/// <param name="QualifiedId">
/// The qualified id, <c>plugin.id::provider-id</c>, consumers store to select the provider. Empty when the
/// host predates video streams and nothing was registered: the plugin keeps running and simply offers no
/// streams there.
/// </param>
/// <param name="ProviderId">The provider-local id it was registered under. Empty together with
/// <paramref name="QualifiedId" />.</param>
public sealed record VideoStreamProviderRegistration(string QualifiedId, string ProviderId);
