namespace MacroDeckHost.Integrations.Discord.Rpc;

internal sealed class DiscordRpcException : Exception
{
	public DiscordRpcException(int code, string message)
		: base(message)
	{
		Code = code;
	}

	public DiscordRpcException(string message)
		: base(message)
	{
	}

	public DiscordRpcException(string message, Exception innerException)
		: base(message, innerException)
	{
	}

	public DiscordRpcException()
	{
	}

	public int Code { get; }

	public bool IsScopeProblem => Code is 4006 or 4007;

	public bool IsAuthProblem => Code is 4009 or 4010;

	public bool IsAlreadyInVoiceChannel => Code == 5003;
}
