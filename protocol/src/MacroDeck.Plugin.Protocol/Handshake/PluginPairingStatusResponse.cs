namespace MacroDeck.Plugin.Protocol.Handshake;

/// <summary>Response of <c>GET /api/plugins/pairing/{requestId}</c>.</summary>
public sealed record PluginPairingStatusResponse
{
	public required string Status { get; init; }

	public required DateTimeOffset ExpiresAt { get; init; }
}
