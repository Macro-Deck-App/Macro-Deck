using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MacroDeckHost.Application.Mcp;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MacroDeckHost.Mcp;

public static class McpConsent
{
	public const string Unavailable = "CONFIRMATION_UNAVAILABLE";
	public const string Declined = "CONFIRMATION_DECLINED";
	public const string Key = "confirm";

	public static Guid TokenId(RequestContext<CallToolRequestParams> context)
	{
		return Guid.TryParse(context.User?.FindFirst(McpClaimTypes.TokenId)?.Value, out var id)
			? id
			: throw McpToolErrors.Create(McpToolErrors.InvalidArgument, "The request carries no MCP token.");
	}

	// The consent is bound to the exact tool call: a retry with other arguments asks again.
	public static string Fingerprint(RequestContext<CallToolRequestParams> context)
	{
		var arguments = JsonSerializer.Serialize(context.Params?.Arguments ?? new Dictionary<string, JsonElement>());
		return Convert.ToHexStringLower(
			SHA256.HashData(Encoding.UTF8.GetBytes(context.Params?.Name + "\n" + arguments)));
	}
}
