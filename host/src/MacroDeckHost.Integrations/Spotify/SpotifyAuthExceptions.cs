namespace MacroDeckHost.Integrations.Spotify;

internal sealed class SpotifyAuthTransientException : Exception
{
	public SpotifyAuthTransientException()
	{
	}

	public SpotifyAuthTransientException(string message)
		: base(message)
	{
	}

	public SpotifyAuthTransientException(string message, Exception innerException)
		: base(message, innerException)
	{
	}

	public bool IsRateLimit { get; init; }

	public TimeSpan? RetryAfter { get; init; }
}

internal sealed class SpotifyAuthRejectedException : Exception
{
	public SpotifyAuthRejectedException()
	{
	}

	public SpotifyAuthRejectedException(string message)
		: base(message)
	{
	}

	public SpotifyAuthRejectedException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
