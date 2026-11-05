namespace MacroDeckHost.Integrations.Discord.Protocol;

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

	// Discord sends a refused scope as a generic OAuth2 error (5000); only its message names the scope.
	public bool IsScopeProblem => Code is 4006 or 4007
		|| Code == 5000 && Message.Contains("invalid_scope", StringComparison.Ordinal);

	public bool IsAuthProblem => Code is 4009 or 4010;

	public bool IsAlreadyInVoiceChannel => Code == 5003;

	public bool IsUnknownCommand => Code == 1000 && Message.StartsWith("Unknown command", StringComparison.Ordinal);
}
