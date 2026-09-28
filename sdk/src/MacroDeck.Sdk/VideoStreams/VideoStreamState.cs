namespace MacroDeck.Sdk.VideoStreams;

/// <summary>A stream's state at its source, independent of any session.</summary>
public enum VideoStreamState
{
	/// <summary>The stream cannot be opened, for example because the source does not offer it right now.</summary>
	Unavailable,

	/// <summary>The provider has no connection to the source.</summary>
	Disconnected,

	/// <summary>The provider is connecting to the source.</summary>
	Connecting,

	/// <summary>The stream can be opened.</summary>
	Connected
}
