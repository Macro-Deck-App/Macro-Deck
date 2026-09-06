namespace MacroDeck.Plugin.Protocol.Handshake;

/// <summary>Body of <c>POST /api/plugins/pairing</c>, unauthenticated - the request itself is what a
/// plugin has instead of a credential.</summary>
public sealed record PluginPairingRequest
{
	/// <summary>Reverse-domain package id, validated by <see cref="PluginId" />.</summary>
	public required string PluginId { get; init; }

	public required string DisplayName { get; init; }

	public required string CodeChallenge { get; init; }

	public required string CodeChallengeMethod { get; init; }

	public PluginPairingClientInfo? Client { get; init; }
}
