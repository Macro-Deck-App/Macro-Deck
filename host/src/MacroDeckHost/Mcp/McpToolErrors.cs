using ModelContextProtocol;

namespace MacroDeckHost.Mcp;

public static class McpToolErrors
{
	public const string InvalidArgument = "INVALID_ARGUMENT";
	public const string NotFound = "NOT_FOUND";

	public static McpException Create(string code, string message)
	{
		return new McpException($"{code}: {message}");
	}
}
