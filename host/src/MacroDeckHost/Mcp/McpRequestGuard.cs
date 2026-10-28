using MacroDeckHost.Auth;

namespace MacroDeckHost.Mcp;

public static class McpRequestGuard
{
	// DNS rebinding: a page in the user's browser must not reach the MCP endpoint or the secret page
	// through a hostname that resolves to loopback, so Host and Origin are checked on every request.
	public static int? Refusal(HttpContext context)
	{
		if (!LoopbackConnection.IsLoopbackListener(context)
			|| context.Connection.RemoteIpAddress is null
			|| !System.Net.IPAddress.IsLoopback(context.Connection.RemoteIpAddress))
		{
			return StatusCodes.Status404NotFound;
		}

		if (!LoopbackConnection.HasLoopbackHost(context.Request)
			|| LoopbackConnection.IsCrossOriginBrowserRequest(context.Request))
		{
			return StatusCodes.Status403Forbidden;
		}

		return null;
	}
}
