namespace MacroDeckHost.Integrations.Twitch.Auth;

internal sealed class TwitchOAuthTransientException : Exception
{
	public TwitchOAuthTransientException()
	{
	}

	public TwitchOAuthTransientException(string message)
		: base(message)
	{
	}

	public TwitchOAuthTransientException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}

internal sealed class TwitchOAuthRejectedException : Exception
{
	public TwitchOAuthRejectedException()
	{
	}

	public TwitchOAuthRejectedException(string message)
		: base(message)
	{
	}

	public TwitchOAuthRejectedException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
