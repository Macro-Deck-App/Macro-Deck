namespace MacroDeckHost.Integrations.StreamlabsDesktop.Protocol;

internal sealed class StreamlabsRpcException : Exception
{
	public StreamlabsRpcException()
	{
	}

	public StreamlabsRpcException(string message)
		: base(message)
	{
	}

	public StreamlabsRpcException(string message, Exception innerException)
		: base(message, innerException)
	{
	}

	public StreamlabsRpcException(string message, int code)
		: base(message)
	{
		Code = code;
	}

	public int Code { get; }
}

internal sealed class StreamlabsAuthenticationException : Exception
{
	public StreamlabsAuthenticationException()
	{
	}

	public StreamlabsAuthenticationException(string message)
		: base(message)
	{
	}

	public StreamlabsAuthenticationException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
