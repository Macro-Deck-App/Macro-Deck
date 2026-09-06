namespace MacroDeck.Plugin.Protocol.Errors;

/// <summary>
/// WebSocket close codes this protocol reserves. An unknown message type or a malformed envelope is
/// never grounds for closing the socket - only these seven conditions are.
/// </summary>
public static class ProtocolCloseCodes
{
	public const int SessionReplaced = 4000;

	public const int ProtocolVersionUnsupported = 4001;

	public const int SessionExpired = 4002;

	public const int AuthenticationFailed = 4003;

	public const int SupervisorShutdown = 4004;

	/// <summary>The host rejected the plugin's declared capabilities at registration (invalid or
	/// duplicated ids, a colliding integration id). Terminal - the plugin must fix its declaration and
	/// reconnect, so this is not a resumable drop.</summary>
	public const int RegistrationRejected = 4005;

	// RFC 6455 reserved range (1013 "Try Again Later"), not a MacroDeck-assigned code.
	public const int QueueOverflow = 1013;

	public static readonly IReadOnlyList<int> All =
	[
		QueueOverflow, SessionReplaced, ProtocolVersionUnsupported, SessionExpired, AuthenticationFailed,
		SupervisorShutdown, RegistrationRejected,
	];
}
