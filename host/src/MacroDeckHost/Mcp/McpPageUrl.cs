namespace MacroDeckHost.Mcp;

public static class McpPageUrl
{
	// The page has to open where the client reached the host, which is another computer's view of it
	// when the client runs elsewhere in the network.
	public static string? Origin(HttpContext? context)
	{
		if (context?.Request.Host.HasValue == true)
		{
			return $"{context.Request.Scheme}://{context.Request.Host}";
		}

		return HostEndpoints.TryGetLoopbackPort(out var port) ? $"http://127.0.0.1:{port}" : null;
	}
}
