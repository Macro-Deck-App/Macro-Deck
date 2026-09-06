namespace MacroDeck.Plugin.Protocol.Handshake;

/// <summary>Body of <c>POST /api/plugins/pairing/{requestId}/redemption</c>. On success, the response
/// is a <see cref="PluginRegistrationResponse" />.</summary>
public sealed record PluginPairingRedemptionRequest
{
	public required string CodeVerifier { get; init; }
}
