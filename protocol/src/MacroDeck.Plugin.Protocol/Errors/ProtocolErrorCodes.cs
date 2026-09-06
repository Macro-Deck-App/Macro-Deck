namespace MacroDeck.Plugin.Protocol.Errors;

/// <summary>
/// Every error code v1 speaks. <c>public const string</c> with a PascalCase name and a SCREAMING_SNAKE
/// value - naming the field after the value would trip CA1707, and this project carries no
/// <c>NoWarn</c>. Append-only within a major: removing or renaming a code requires the current
/// protocol version to advance.
/// </summary>
public static class ProtocolErrorCodes
{
	public const string ProtocolVersionUnsupported = "PROTOCOL_VERSION_UNSUPPORTED";

	public const string UnknownMessageType = "UNKNOWN_MESSAGE_TYPE";

	public const string MalformedEnvelope = "MALFORMED_ENVELOPE";

	public const string InvalidPayload = "INVALID_PAYLOAD";

	public const string Unauthenticated = "UNAUTHENTICATED";

	public const string PluginAlreadyRegistered = "PLUGIN_ALREADY_REGISTERED";

	public const string SessionExpired = "SESSION_EXPIRED";

	public const string SessionNotResumable = "SESSION_NOT_RESUMABLE";

	public const string SessionReplaced = "SESSION_REPLACED";

	// Added for the ui host api: a provider pushing a tree, patch or fault for a session the host is not
	// brokering for it needs to be told which of its own sessions is gone, and CAPABILITY_UNAVAILABLE
	// would say the whole capability went away instead.
	public const string SessionNotFound = "SESSION_NOT_FOUND";

	public const string CapabilityUnsupported = "CAPABILITY_UNSUPPORTED";

	public const string CapabilityUnavailable = "CAPABILITY_UNAVAILABLE";

	public const string PayloadTooLarge = "PAYLOAD_TOO_LARGE";

	public const string AssetTooLarge = "ASSET_TOO_LARGE";

	public const string QueueOverflow = "QUEUE_OVERFLOW";

	public const string RateLimited = "RATE_LIMITED";

	public const string Timeout = "TIMEOUT";

	public const string Cancelled = "CANCELLED";

	public const string CorrelationUnknown = "CORRELATION_UNKNOWN";

	public const string DuplicateIdempotencyKey = "DUPLICATE_IDEMPOTENCY_KEY";

	public const string InternalError = "INTERNAL_ERROR";

	public static readonly IReadOnlyList<string> All =
	[
		ProtocolVersionUnsupported, UnknownMessageType, MalformedEnvelope, InvalidPayload, Unauthenticated,
		PluginAlreadyRegistered, SessionExpired, SessionNotResumable, SessionReplaced, SessionNotFound,
		CapabilityUnsupported,
		CapabilityUnavailable, PayloadTooLarge, AssetTooLarge, QueueOverflow, RateLimited,
		Timeout, Cancelled, CorrelationUnknown, DuplicateIdempotencyKey, InternalError,
	];
}
