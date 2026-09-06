namespace MacroDeckHost.Integrations.Discord.Rpc;

internal sealed class DiscordIpcUnavailableException : IOException
{
	public DiscordIpcUnavailableException(string message, bool accessDenied = false)
		: base(message)
	{
		AccessDenied = accessDenied;
	}

	public bool AccessDenied { get; }
}
