namespace MacroDeck.Sdk.VideoStreams;

/// <summary>Why a video stream operation failed.</summary>
public enum VideoStreamErrorCode
{
	/// <summary>This Macro Deck, or this provider, does not support the operation.</summary>
	Unsupported,

	UnknownProvider,

	UnknownStream,

	/// <summary>No session with that id is open, or it was already closed.</summary>
	UnknownSession,

	/// <summary>The stream exists but cannot be served right now.</summary>
	StreamUnavailable,

	/// <summary>The provider serves none of the transports the consumer accepts.</summary>
	TransportNotAccepted,

	/// <summary>The provider cannot open another session right now.</summary>
	CapacityReached,

	/// <summary>The provider is busy. Retrying later can succeed.</summary>
	Busy,

	/// <summary>Any other failure.</summary>
	Failed
}
