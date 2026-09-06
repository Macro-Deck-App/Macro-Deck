namespace MacroDeck.Plugin.Protocol.Errors;

/// <summary>
/// Default English text for each <see cref="ProtocolErrorCodes" /> value, with no interpolation slots
/// so a code can never carry a credential or a path into user-facing text. These are defaults keyed by
/// code - the Angular UI localises from the code, not this string.
/// </summary>
public static class ProtocolErrorMessages
{
	private const string FallbackMessage = "An unspecified protocol error occurred.";

	private static readonly Dictionary<string, string> _messages =
		new(StringComparer.Ordinal)
		{
			[ProtocolErrorCodes.ProtocolVersionUnsupported] = "The requested protocol version is not supported.",
			[ProtocolErrorCodes.UnknownMessageType] = "The message type is not recognised.",
			[ProtocolErrorCodes.MalformedEnvelope] = "The message envelope could not be parsed.",
			[ProtocolErrorCodes.InvalidPayload] = "The message payload does not match the expected shape.",
			[ProtocolErrorCodes.Unauthenticated] = "Authentication failed.",
			[ProtocolErrorCodes.PluginAlreadyRegistered] = "A plugin is already registered with this identity.",
			[ProtocolErrorCodes.SessionExpired] = "The session has expired.",
			[ProtocolErrorCodes.SessionNotResumable] = "The session can no longer be resumed.",
			[ProtocolErrorCodes.SessionReplaced] = "The session was replaced by a newer connection.",
			[ProtocolErrorCodes.SessionNotFound] = "No session matches that id.",
			[ProtocolErrorCodes.CapabilityUnsupported] = "The capability kind is not supported.",
			[ProtocolErrorCodes.CapabilityUnavailable] = "The capability is not currently available.",
			[ProtocolErrorCodes.PayloadTooLarge] = "The message payload exceeds the allowed size.",
			[ProtocolErrorCodes.AssetTooLarge] = "The asset exceeds the allowed size.",
			[ProtocolErrorCodes.QueueOverflow] = "The message queue overflowed.",
			[ProtocolErrorCodes.RateLimited] = "Too many requests; retry after the given delay.",
			[ProtocolErrorCodes.Timeout] = "The operation timed out.",
			[ProtocolErrorCodes.Cancelled] = "The operation was cancelled.",
			[ProtocolErrorCodes.CorrelationUnknown] = "No in-flight message matches this correlation id.",
			[ProtocolErrorCodes.DuplicateIdempotencyKey] = "This idempotency key is already in flight.",
			[ProtocolErrorCodes.InternalError] = "An internal error occurred.",
		};

	public static string For(string code)
		=> _messages.TryGetValue(code, out var message) ? message : FallbackMessage;
}
