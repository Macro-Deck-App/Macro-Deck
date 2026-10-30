namespace MacroDeckHost.Mcp;

public static class McpPaths
{
	public const string Endpoint = "/mcp";
	public const string SecretEntry = "/mcp/secret-entry";
	public const string Confirmation = "/mcp/confirm";
	public const string SessionHeader = "Mcp-Session-Id";

	public static bool IsMcpRequest(PathString path)
	{
		return path.StartsWithSegments(Endpoint);
	}

	public static bool IsTransportRequest(PathString path)
	{
		return path.StartsWithSegments(Endpoint)
			&& !path.StartsWithSegments(SecretEntry)
			&& !path.StartsWithSegments(Confirmation);
	}
}
