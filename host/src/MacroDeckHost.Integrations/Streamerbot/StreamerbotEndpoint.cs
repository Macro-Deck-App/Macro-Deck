using System.Globalization;

namespace MacroDeckHost.Integrations.Streamerbot;

internal static class StreamerbotEndpoint
{
	public const string DefaultHost = "127.0.0.1";
	public const int DefaultPort = 8080;
	public const string DefaultEndpoint = "/";

	public static Uri Build(string host, int port, string? endpoint)
	{
		var builder = new UriBuilder("ws", NormaliseHost(host), port, NormaliseEndpoint(endpoint));
		return builder.Uri;
	}

	public static string NormaliseHost(string host)
	{
		var value = host.Trim();
		foreach (var scheme in (string[])["ws://", "wss://", "http://", "https://"])
		{
			if (value.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
			{
				value = value[scheme.Length..];
				break;
			}
		}

		var slash = value.IndexOf('/', StringComparison.Ordinal);
		if (slash >= 0)
		{
			value = value[..slash];
		}

		var colon = value.LastIndexOf(':');
		if (colon > 0 && int.TryParse(value[(colon + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
		{
			value = value[..colon];
		}

		return value.Length == 0 ? DefaultHost : value;
	}

	public static string NormaliseEndpoint(string? endpoint)
	{
		var value = (endpoint ?? string.Empty).Trim();
		if (value.Length == 0)
		{
			return DefaultEndpoint;
		}

		return value.StartsWith('/') ? value : "/" + value;
	}
}
