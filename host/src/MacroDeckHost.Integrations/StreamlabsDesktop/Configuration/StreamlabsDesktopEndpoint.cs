using System.Globalization;

namespace MacroDeckHost.Integrations.StreamlabsDesktop.Configuration;

internal sealed record StreamlabsDesktopEndpoint(string Host, int Port)
{
	public const string DefaultHost = "127.0.0.1";

	public const int DefaultPort = 59650;

	private const string Path = "/api/websocket";

	public string DisplayAddress => string.Create(CultureInfo.InvariantCulture, $"{Host}:{Port}");

	public static StreamlabsDesktopEndpoint Create(string? host, int? port)
	{
		return new StreamlabsDesktopEndpoint(string.IsNullOrWhiteSpace(host) ? DefaultHost : host.Trim(),
			port ?? DefaultPort);
	}

	public Uri WebSocketUri()
	{
		return new Uri(string.Create(CultureInfo.InvariantCulture, $"ws://{Host}:{Port}{Path}"));
	}
}
