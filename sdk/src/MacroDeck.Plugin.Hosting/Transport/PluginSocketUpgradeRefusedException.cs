using System.Net;

namespace MacroDeck.Plugin.Hosting.Transport;

internal sealed class PluginSocketUpgradeRefusedException(HttpStatusCode statusCode, Exception innerException)
	: Exception(innerException.Message, innerException)
{
	public HttpStatusCode StatusCode { get; } = statusCode;
}
