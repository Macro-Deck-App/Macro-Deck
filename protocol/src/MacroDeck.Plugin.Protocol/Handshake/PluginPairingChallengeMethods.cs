namespace MacroDeck.Plugin.Protocol.Handshake;

/// <summary>The PKCE code challenge methods a pairing request may declare.</summary>
public static class PluginPairingChallengeMethods
{
	public const string S256 = "S256";

	public static readonly IReadOnlyList<string> All =
	[
		S256,
	];
}
