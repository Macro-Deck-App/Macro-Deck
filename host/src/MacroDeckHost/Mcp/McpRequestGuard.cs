using MacroDeckHost.Auth;

namespace MacroDeckHost.Mcp;

public static class McpRequestGuard
{
	// Every listener needs an MCP token. Browser pages are refused so that a page on the network, or a
	// hostname rebound to this computer, can never drive the endpoint or open its one-time pages.
	public static int? Refusal(HttpContext context)
	{
		var loopbackListener = LoopbackConnection.IsLoopbackListener(context);
		if (loopbackListener
			&& (context.Connection.RemoteIpAddress is null
				|| !System.Net.IPAddress.IsLoopback(context.Connection.RemoteIpAddress)))
		{
			return StatusCodes.Status404NotFound;
		}

		if (loopbackListener && !LoopbackConnection.HasLoopbackHost(context.Request)
			|| LoopbackConnection.IsCrossOriginBrowserRequest(context.Request))
		{
			return StatusCodes.Status403Forbidden;
		}

		return null;
	}
}
