namespace MacroDeckHost.Integrations.Twitch.Protocol;

internal sealed class TwitchEventSubException : Exception
{
	public TwitchEventSubException()
	{
	}

	public TwitchEventSubException(string message)
		: base(message)
	{
	}

	public TwitchEventSubException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
