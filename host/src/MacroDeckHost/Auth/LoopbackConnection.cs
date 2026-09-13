using System.Net;

namespace MacroDeckHost.Auth;

public static class LoopbackConnection
{
	public static bool IsTrusted(HttpContext context)
	{
		var connection = context.Connection;
		return IsLoopbackListener(context) &&
			connection.RemoteIpAddress is not null &&
			IPAddress.IsLoopback(connection.RemoteIpAddress) &&
			HasLoopbackHost(context.Request) &&
			!IsCrossOriginBrowserRequest(context.Request);
	}

	public static bool IsLoopbackListener(HttpContext context)
		=> HostEndpoints.TryGetLoopbackPort(out var loopbackPort) && context.Connection.LocalPort == loopbackPort;

	public static bool IsLocalRequest(HttpContext context)
	{
		var connection = context.Connection;
		return connection.RemoteIpAddress is not null &&
			IPAddress.IsLoopback(connection.RemoteIpAddress) &&
			HasLoopbackHost(context.Request);
	}

	// Loopback trust needs no credentials, so a page open in the user's browser must not borrow it.
	// Sec-Fetch-Site is set by the browser itself; without it, an Origin other than our own is foreign.
	private static bool IsCrossOriginBrowserRequest(HttpRequest request)
	{
		var site = request.Headers["Sec-Fetch-Site"].ToString();
		if (site.Length > 0)
		{
			return !string.Equals(site, "same-origin", StringComparison.OrdinalIgnoreCase) &&
				!string.Equals(site, "none", StringComparison.OrdinalIgnoreCase);
		}

		var origin = request.Headers.Origin.ToString();
		return origin.Length > 0 &&
			!string.Equals(origin, $"{request.Scheme}://{request.Host}", StringComparison.OrdinalIgnoreCase);
	}

	private static bool HasLoopbackHost(HttpRequest request)
	{
		var host = request.Host.Host;
		return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
			(IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));
	}
}
