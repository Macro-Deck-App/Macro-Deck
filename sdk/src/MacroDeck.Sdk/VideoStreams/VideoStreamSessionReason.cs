namespace MacroDeck.Sdk.VideoStreams;

/// <summary>Why a session changed state or closed.</summary>
public enum VideoStreamSessionReason
{
	/// <summary>No particular reason.</summary>
	None,

	/// <summary>The provider lost its source connection and is reconnecting.</summary>
	ProviderReconnecting,

	/// <summary>The source stopped delivering the stream.</summary>
	SourceLost,

	/// <summary>The source delivers the stream again.</summary>
	SourceRecovered,

	/// <summary>The provider ended the session.</summary>
	ProviderClosed,

	/// <summary>
	/// The provider was withdrawn, its integration stopped, or its plugin disconnected. Retryable: the
	/// consumer can open a new session once the provider is listed again.
	/// </summary>
	ProviderRemoved,

	/// <summary>The consumer closed the session.</summary>
	ConsumerClosed,

	/// <summary>The consumer stopped renewing the session.</summary>
	LeaseExpired,

	/// <summary>The consumer's connection to Macro Deck ended.</summary>
	ConsumerDisconnected,

	/// <summary>The plugin lost its connection to Macro Deck; the session was opened on the earlier connection.</summary>
	HostDisconnected,

	/// <summary>Macro Deck is shutting down.</summary>
	HostShutdown,

	/// <summary>The session failed.</summary>
	Failed
}
