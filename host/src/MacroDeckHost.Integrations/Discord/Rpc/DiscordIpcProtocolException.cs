namespace MacroDeckHost.Integrations.Discord.Rpc;

internal sealed class DiscordIpcProtocolException : IOException
{
	public DiscordIpcProtocolException(string message)
		: base(message)
	{
	}

	public DiscordIpcProtocolException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
