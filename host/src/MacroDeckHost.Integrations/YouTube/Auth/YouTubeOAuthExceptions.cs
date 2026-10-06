namespace MacroDeckHost.Integrations.YouTube.Auth;

internal sealed class YouTubeOAuthTransientException : Exception
{
	public YouTubeOAuthTransientException()
	{
	}

	public YouTubeOAuthTransientException(string message)
		: base(message)
	{
	}

	public YouTubeOAuthTransientException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}

internal sealed class YouTubeOAuthRejectedException : Exception
{
	public YouTubeOAuthRejectedException()
	{
	}

	public YouTubeOAuthRejectedException(string message)
		: base(message)
	{
	}

	public YouTubeOAuthRejectedException(string message, Exception innerException)
		: base(message, innerException)
	{
	}

	public YouTubeOAuthRejectedException(string message, string? errorCode)
		: base(message)
	{
		ErrorCode = errorCode;
	}

	public string? ErrorCode { get; }
}
