namespace MacroDeckHost.Integrations.Discord.Rpc;

internal sealed class DiscordIpcUnavailableException : IOException
{
	public DiscordIpcUnavailableException(string message, bool accessDenied = false, bool richPresenceOnly = false)
		: base(message)
	{
		AccessDenied = accessDenied;
		RichPresenceOnly = richPresenceOnly;
	}

	public bool AccessDenied { get; }

	public bool RichPresenceOnly { get; }
}
