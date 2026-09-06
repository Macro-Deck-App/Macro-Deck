using System.Globalization;

namespace MacroDeckHost.Integrations.StreamlabsDesktop;

internal sealed record StreamlabsDesktopEndpoint(string Host, int Port)
{
	public const string DefaultHost = "127.0.0.1";

	public const int DefaultPort = 59650;

	private const string Path = "/api/websocket";

	public string DisplayAddress => string.Create(CultureInfo.InvariantCulture, $"{Host}:{Port}");

	public static StreamlabsDesktopEndpoint Create(string? host, int? port)
		=> new(string.IsNullOrWhiteSpace(host) ? DefaultHost : host.Trim(), port ?? DefaultPort);

	public Uri WebSocketUri() => new(string.Create(CultureInfo.InvariantCulture, $"ws://{Host}:{Port}{Path}"));
}
