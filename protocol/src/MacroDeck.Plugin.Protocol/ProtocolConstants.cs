namespace MacroDeck.Plugin.Protocol;

/// <summary>
/// REST paths, the WebSocket upgrade path and the wire media type. Paths are unversioned - the
/// protocol version is negotiated over <c>POST /sessions</c>, never encoded in the URL.
/// </summary>
public static class ProtocolConstants
{
	public const string ApiBasePath = "/api/plugins";

	public const string ProtocolDiscoveryPath = "/api/plugins/protocol";

	public const string RegistrationPath = "/api/plugins/registration";

	public const string PairingPath = "/api/plugins/pairing";

	public const string SessionsPath = "/api/plugins/sessions";

	public const string WebSocketPath = "/plugins/ws";

	public const string WebSocketSubProtocol = "macrodeck.plugin.v1";

	public const string JsonMediaType = "application/json";

	public static readonly IReadOnlyList<string> All =
	[
		ApiBasePath, ProtocolDiscoveryPath, RegistrationPath, PairingPath, SessionsPath, WebSocketPath,
	];
}
