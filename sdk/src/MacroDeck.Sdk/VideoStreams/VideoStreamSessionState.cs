namespace MacroDeck.Sdk.VideoStreams;

/// <summary>The state of one consumer's session.</summary>
public enum VideoStreamSessionState
{
	/// <summary>The session is being opened, or the provider is still negotiating it.</summary>
	Opening,

	/// <summary>The consumer can play the stream from the current description.</summary>
	Active,

	/// <summary>The consumer stopped showing the stream for now.</summary>
	Suspended,

	/// <summary>The session is interrupted and the provider is trying to recover it.</summary>
	Reconnecting
}
