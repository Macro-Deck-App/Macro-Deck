namespace MacroDeckHost.Integrations.Twitch.Protocol;

internal sealed class TwitchAuthorizationRevokedException : Exception
{
	public TwitchAuthorizationRevokedException()
	{
	}

	public TwitchAuthorizationRevokedException(string message)
		: base(message)
	{
	}

	public TwitchAuthorizationRevokedException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
