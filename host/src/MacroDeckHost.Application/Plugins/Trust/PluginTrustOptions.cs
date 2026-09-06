namespace MacroDeckHost.Application.Plugins.Trust;

public sealed record PluginTrustOptions
{
	public static readonly PluginTrustOptions Default = new();

	/// <summary>Overrides the root public key package signatures are verified against.
	/// <see langword="null" /> (the default) anchors to the pinned Macro Deck root key. Tests anchor to a
	/// throwaway root instead so they never depend on - or risk verifying against - the real key.</summary>
	public ReadOnlyMemory<byte>? RootPublicKeyOverride { get; init; }
}
