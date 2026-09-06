namespace MacroDeckHost.Integrations.Discord.Actions;

internal sealed class DiscordVoiceActionException : Exception
{
	public DiscordVoiceActionException(string message)
		: base(message)
	{
	}

	public DiscordVoiceActionException(string message, Exception innerException)
		: base(message, innerException)
	{
	}

	public DiscordVoiceActionException()
	{
	}
}
