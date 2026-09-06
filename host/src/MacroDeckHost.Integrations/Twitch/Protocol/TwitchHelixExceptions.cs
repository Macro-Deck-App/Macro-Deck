namespace MacroDeckHost.Integrations.Twitch.Protocol;

internal sealed class TwitchScopeException : Exception
{
	public TwitchScopeException()
	{
	}

	public TwitchScopeException(string message)
		: base(message)
	{
	}

	public TwitchScopeException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}

internal sealed class TwitchRequestException : Exception
{
	public TwitchRequestException()
	{
	}

	public TwitchRequestException(string message)
		: base(message)
	{
	}

	public TwitchRequestException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
