namespace MacroDeck.Plugin.Protocol.Handshake;

/// <summary>Response of <c>POST /api/plugins/pairing</c>.</summary>
public sealed record PluginPairingResponse
{
	/// <summary>Identifies the request but is not a credential and authorises nothing - it is safe in
	/// a URL, and the verifier is what proves possession.</summary>
	public required string RequestId { get; init; }

	public required DateTimeOffset ExpiresAt { get; init; }

	public required int PollIntervalSeconds { get; init; }
}
