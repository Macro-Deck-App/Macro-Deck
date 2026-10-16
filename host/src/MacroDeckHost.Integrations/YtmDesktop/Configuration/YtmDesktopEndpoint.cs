using System.Globalization;

namespace MacroDeckHost.Integrations.YtmDesktop.Configuration;

internal sealed record YtmDesktopEndpoint(string Host, int Port)
{
	public const string DefaultHost = "127.0.0.1";
	public const int DefaultPort = 9863;

	public static string NormaliseHost(string? host)
	{
		var value = (host ?? string.Empty).Trim();
		if (value.Length == 0)
		{
			return DefaultHost;
		}

		return string.Equals(value, "localhost", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(value, "::1", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(value, "[::1]", StringComparison.OrdinalIgnoreCase)
				? DefaultHost
				: value;
	}

	public static YtmDesktopEndpoint Create(string? host, int port)
	{
		return new YtmDesktopEndpoint(NormaliseHost(host), port is > 0 and <= 65535 ? port : DefaultPort);
	}

	public Uri MetadataUri()
	{
		return new UriBuilder(Uri.UriSchemeHttp, Host, Port, "/metadata").Uri;
	}

	public Uri ApiUri(string relativePath)
	{
		return new UriBuilder(Uri.UriSchemeHttp, Host, Port, $"/api/v1/{relativePath}").Uri;
	}

	public Uri RealtimeUri()
	{
		return new UriBuilder("ws", Host, Port, "/socket.io/") { Query = "EIO=4&transport=websocket" }.Uri;
	}

	public string DisplayAddress => string.Create(CultureInfo.InvariantCulture, $"{Host}:{Port}");
}
