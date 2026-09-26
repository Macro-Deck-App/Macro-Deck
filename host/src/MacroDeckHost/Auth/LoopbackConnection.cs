using System.Net;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Usb;

namespace MacroDeckHost.Auth;

public static class LoopbackConnection
{
	public static bool IsTrusted(HttpContext context)
		=> IsLoopbackTransport(context) &&
			!IsCrossOriginBrowserRequest(context.Request) &&
			HasLoopbackCredential(context);

	public static bool IsLoopbackTransport(HttpContext context)
	{
		var connection = context.Connection;
		return IsLoopbackListener(context) &&
			connection.RemoteIpAddress is not null &&
			IPAddress.IsLoopback(connection.RemoteIpAddress) &&
			HasLoopbackHost(context.Request);
	}

	public static string SessionCookieName(HttpContext context) => $"md_loopback_{AuthCookies.ListenerPort(context)}";

	public static bool IsLoopbackListener(HttpContext context)
		=> HostEndpoints.TryGetLoopbackPort(out var loopbackPort) && context.Connection.LocalPort == loopbackPort;

	// A connection bridged from a USB link without debugging is not local, although it comes from loopback.
	public static bool IsLocalRequest(HttpContext context)
	{
		var connection = context.Connection;
		return context.Features.Get<IBridgedConnectionFeature>() is null &&
			connection.RemoteIpAddress is not null &&
			IPAddress.IsLoopback(connection.RemoteIpAddress) &&
			HasLoopbackHost(context.Request);
	}

	// A page in the user's browser must not ride on the desktop session cookie or a proxied secret.
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

	private static bool HasLoopbackCredential(HttpContext context)
		=> LoopbackSecret.MatchesHeader(context.Request.Headers[LoopbackSecret.HeaderName].ToString()) ||
			LoopbackSecret.MatchesSessionCookie(context.Request.Cookies[SessionCookieName(context)]);

	private static bool HasLoopbackHost(HttpRequest request)
	{
		var host = request.Host.Host;
		return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
			(IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));
	}
}
