namespace MacroDeckHost.Integrations.HomeAssistant.Protocol;

internal sealed class HomeAssistantRequestException : Exception
{
	public HomeAssistantRequestException(string message)
		: base(message)
	{
		Code = string.Empty;
	}

	public HomeAssistantRequestException(string message, Exception innerException)
		: base(message, innerException)
	{
		Code = string.Empty;
	}

	public HomeAssistantRequestException(string code, string? message)
		: base($"Home Assistant rejected the command: {message ?? code}")
	{
		Code = code;
	}

	public string Code { get; }
}

internal sealed class HomeAssistantAuthenticationException : Exception
{
	public HomeAssistantAuthenticationException(string message)
		: base(message)
	{
	}

	public HomeAssistantAuthenticationException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}

internal sealed class HomeAssistantTlsException : Exception
{
	public HomeAssistantTlsException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
