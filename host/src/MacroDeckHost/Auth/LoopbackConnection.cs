using System.Net;

namespace MacroDeckHost.Auth;

public static class LoopbackConnection
{
	public static bool IsTrusted(HttpContext context)
	{
		var connection = context.Connection;
		return HostEndpoints.TryGetLoopbackPort(out var loopbackPort) &&
			connection.LocalPort == loopbackPort &&
			connection.RemoteIpAddress is not null &&
			IPAddress.IsLoopback(connection.RemoteIpAddress) &&
			HasLoopbackHost(context.Request);
	}

	public static bool IsLocalRequest(HttpContext context)
	{
		var connection = context.Connection;
		return connection.RemoteIpAddress is not null &&
			IPAddress.IsLoopback(connection.RemoteIpAddress) &&
			HasLoopbackHost(context.Request);
	}

	private static bool HasLoopbackHost(HttpRequest request)
	{
		var host = request.Host.Host;
		return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
			(IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));
	}
}
