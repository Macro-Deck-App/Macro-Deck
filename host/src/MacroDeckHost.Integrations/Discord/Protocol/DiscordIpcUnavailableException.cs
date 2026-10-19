namespace MacroDeckHost.Integrations.Discord.Protocol;

internal sealed class DiscordIpcUnavailableException : IOException
{
	public DiscordIpcUnavailableException(
		string message,
		DiscordIpcDenial? denial = null,
		bool richPresenceOnly = false)
		: base(message)
	{
		Denial = denial;
		RichPresenceOnly = richPresenceOnly;
	}

	public DiscordIpcDenial? Denial { get; }

	public bool AccessDenied => Denial is not null;

	public bool RichPresenceOnly { get; }
}
