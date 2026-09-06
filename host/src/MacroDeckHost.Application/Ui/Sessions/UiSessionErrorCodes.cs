namespace MacroDeckHost.Application.Ui.Sessions;

// Host-to-client vocabulary, deliberately separate from ProtocolErrorCodes, which is the
// host-to-plugin contract. Where a code means the same thing on both sides the same spelling is used so
// a plugin author and a client developer read one word, not two.
public static class UiSessionErrorCodes
{
	public const string SessionNotFound = "SESSION_NOT_FOUND";

	public const string SessionClosed = "SESSION_CLOSED";

	public const string SessionBusy = "SESSION_BUSY";

	public const string SessionFull = "SESSION_FULL";

	public const string SessionForbidden = "SESSION_FORBIDDEN";

	public const string ProviderDisconnected = "PROVIDER_DISCONNECTED";

	public const string ProviderUnavailable = "PROVIDER_UNAVAILABLE";

	public const string ProviderFaulted = "PROVIDER_FAULTED";

	public const string ProviderTimeout = "PROVIDER_TIMEOUT";

	public const string ProviderRejected = "PROVIDER_REJECTED";

	public const string PayloadTooLarge = "PAYLOAD_TOO_LARGE";

	public const string RateLimited = "RATE_LIMITED";

	public const string InvalidPayload = "INVALID_PAYLOAD";

	public const string TooManySessions = "TOO_MANY_SESSIONS";

	/// <summary>The widget's stored configuration was saved and the session serving it had built that
	/// configuration into its tree, so the tree can only be brought up to date by building a new one.
	/// Always retryable: reopening is exactly what is being asked for.</summary>
	public const string WidgetReconfigured = "WIDGET_RECONFIGURED";
}
