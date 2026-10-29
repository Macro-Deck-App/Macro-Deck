using System.Net;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Usb;
using Microsoft.AspNetCore.HttpOverrides;

namespace MacroDeckHost.Auth;

public static class ReverseProxyForwarding
{
	private static readonly string[] _forwardingHeaders =
		["X-Forwarded-For", "Forwarded", "X-Real-IP", "X-Original-For"];

	public static IApplicationBuilder UseTrustedReverseProxy(this IApplicationBuilder app)
	{
		var options = new ForwardedHeadersOptions
		{
			ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
			ForwardLimit = 1
		};

		// Cleared so the predicate below alone decides which peer is the proxy: the default list does not
		// match an IPv4 proxy reaching the dual-mode listener as ::ffff:127.0.0.1.
		options.KnownProxies.Clear();
		options.KnownIPNetworks.Clear();

		return app.UseWhen(IsFromTrustedProxy, branch => branch.UseForwardedHeaders(options));
	}

	public static bool CarriesForwardingHeaders(HttpRequest request)
	{
		return _forwardingHeaders.Any(header => request.Headers.ContainsKey(header));
	}

	// Only a proxy on this computer is trusted, never the loopback listener the desktop app uses and never
	// a USB bridged connection, which arrives from loopback but is a device that can set any header.
	private static bool IsFromTrustedProxy(HttpContext context)
	{
		var remote = context.Connection.RemoteIpAddress;
		return ReverseProxyTrust.Enabled
			&& remote is not null
			&& IPAddress.IsLoopback(remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote)
			&& !LoopbackConnection.IsLoopbackListener(context)
			&& context.Features.Get<IBridgedConnectionFeature>() is null;
	}
}
