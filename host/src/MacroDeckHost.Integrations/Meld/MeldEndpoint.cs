using System.Net;

namespace MacroDeckHost.Integrations.Meld;

internal static class MeldEndpoint
{
	public const string DefaultHost = "127.0.0.1";
	public const int DefaultPort = 13376;

	public static Uri Build(string host, int port)
	{
		var builder = new UriBuilder("ws", host.Trim(), port);
		return builder.Uri;
	}

	public static bool IsLoopback(string host)
	{
		var value = host.Trim();
		if (string.Equals(value, "localhost", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		return IPAddress.TryParse(value, out var address) && IPAddress.IsLoopback(address);
	}
}
