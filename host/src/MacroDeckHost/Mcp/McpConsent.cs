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
	private const string Key = "confirm";

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

	public static void Require(
		McpServer server,
		RequestContext<CallToolRequestParams> context,
		IReadOnlyList<string> details,
		string message,
		string title)
	{
		if (details.Count == 0)
		{
			return;
		}

		var fingerprint = Fingerprint(context);
		if (context.Params?.RequestState == fingerprint && McpElicitation.Answer(context, Key) is { } answer)
		{
			if (answer.IsAccepted
				&& answer.Content?.TryGetValue(Key, out var confirmed) == true
				&& confirmed.ValueKind == JsonValueKind.True)
			{
				return;
			}

			throw McpToolErrors.Create(Declined, "The user did not confirm.");
		}

		if (server.ClientCapabilities?.Elicitation is not { } elicitation
			|| elicitation.Form is null && elicitation.Url is not null)
		{
			throw McpToolErrors.Create(Unavailable,
				"This needs the user's confirmation, and the client does not support form elicitation: "
				+ string.Join(", ", details));
		}

		throw McpElicitation.Ask(Key,
			new ElicitRequestParams
			{
				Mode = "form",
				Message = message,
				RequestedSchema = new ElicitRequestParams.RequestSchema
				{
					Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
					{
						[Key] = new ElicitRequestParams.BooleanSchema { Title = title, Default = false }
					},
					Required = [Key]
				}
			},
			fingerprint);
	}
}
