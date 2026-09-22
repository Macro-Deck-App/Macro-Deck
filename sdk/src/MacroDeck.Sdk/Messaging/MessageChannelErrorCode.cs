namespace MacroDeck.Sdk.Messaging;

public enum MessageChannelErrorCode
{
	/// <summary>A failure this SDK version does not recognise.</summary>
	Unknown = 0,

	/// <summary>This Macro Deck, or this context, offers no message channel.</summary>
	Unsupported = 1,

	/// <summary>The topic or pattern does not follow the <see cref="MessageTopic" /> grammar.</summary>
	InvalidTopic = 2,

	/// <summary>The payload is larger than <see cref="MessageTopic.MaxPayloadBytes" /> once serialized.</summary>
	PayloadTooLarge = 3,

	/// <summary>Nothing handles the topic.</summary>
	NoHandler = 4,

	/// <summary>The topic has a handler that cannot be reached right now. Retrying later can succeed.</summary>
	HandlerUnavailable = 5,

	/// <summary>Another participant already handles the topic. <see cref="MessageChannelException.HandlerOwner" /> names it.</summary>
	TopicAlreadyHandled = 6,

	/// <summary>The handler failed. Its error is not passed on.</summary>
	HandlerFailed = 7,

	/// <summary>The handler did not answer in time.</summary>
	Timeout = 8,

	/// <summary>Too many messages in quick succession. Retry later.</summary>
	RateLimited = 9,

	/// <summary>There is no connection to Macro Deck right now.</summary>
	NotConnected = 10
}
