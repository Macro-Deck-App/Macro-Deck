using System.Globalization;
using System.Net;

namespace MacroDeckHost.Integrations.HomeAssistant;

internal static class HomeAssistantEndpoint
{
	public const string ExampleUrl = "http://homeassistant.local:8123";

	public const string WebSocketPath = "/api/websocket";

	private static readonly string[] _localSuffixes = [".local", ".lan", ".home", ".internal"];

	public static Uri? TryBuild(string? baseUrl)
	{
		var value = (baseUrl ?? string.Empty).Trim();

		var cut = value.IndexOfAny(['?', '#']);
		if (cut >= 0)
		{
			value = value[..cut].Trim();
		}

		if (value.Length == 0)
		{
			return null;
		}

		if (!value.Contains("://", StringComparison.Ordinal))
		{
			value = (IsLocalName(HostOf(value)) ? "http://" : "https://") + value;
		}

		if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) || string.IsNullOrEmpty(parsed.Host))
		{
			return null;
		}

		var scheme = parsed.Scheme switch
		{
			"ws" or "wss" => parsed.Scheme,
			"http" => "ws",
			"https" => "wss",
			_ => null
		};

		if (scheme is null)
		{
			return null;
		}

		var path = parsed.AbsolutePath.TrimEnd('/');
		if (!path.EndsWith(WebSocketPath, StringComparison.Ordinal))
		{
			path += WebSocketPath;
		}

		var builder = new UriBuilder(scheme, parsed.Host, parsed.IsDefaultPort ? -1 : parsed.Port, path);
		return builder.Uri;
	}

	private static string HostOf(string value)
	{
		var slash = value.IndexOf('/', StringComparison.Ordinal);
		var authority = slash >= 0 ? value[..slash] : value;

		var at = authority.LastIndexOf('@');
		if (at >= 0)
		{
			authority = authority[(at + 1)..];
		}

		if (authority.StartsWith('['))
		{
			var close = authority.IndexOf(']', StringComparison.Ordinal);
			return close > 0 ? authority[1..close] : authority;
		}

		var colon = authority.LastIndexOf(':');
		return colon > 0 &&
			int.TryParse(authority[(colon + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
				? authority[..colon]
				: authority;
	}

	private static bool IsLocalName(string host)
	{
		if (host.Length == 0 ||
			string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
			IPAddress.TryParse(host, out _))
		{
			return true;
		}

		foreach (var suffix in _localSuffixes)
		{
			if (host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}
}
