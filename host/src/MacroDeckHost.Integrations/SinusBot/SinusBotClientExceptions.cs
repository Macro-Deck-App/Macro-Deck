using System.Net;

namespace MacroDeckHost.Integrations.SinusBot;

internal sealed class SinusBotApiException : Exception
{
	public SinusBotApiException(string message, HttpStatusCode? statusCode = null)
		: base(message)
	{
		StatusCode = statusCode;
	}

	public SinusBotApiException(string message, Exception innerException, HttpStatusCode? statusCode = null)
		: base(message, innerException)
	{
		StatusCode = statusCode;
	}

	public HttpStatusCode? StatusCode { get; }
}

internal sealed class SinusBotAuthException : Exception
{
	public SinusBotAuthException(string message)
		: base(message)
	{
	}

	public SinusBotAuthException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
