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
	public const string Pending = "CONFIRMATION_PENDING";
	public const string Key = "confirm";

	public static Guid TokenId(RequestContext<CallToolRequestParams> context)
	{
		return Guid.TryParse(context.User?.FindFirst(McpClaimTypes.TokenId)?.Value, out var id)
			? id
			: throw McpToolErrors.Create(McpToolErrors.InvalidArgument, "The request carries no MCP token.");
	}

	// The consent is bound to the exact tool call: a retry with other arguments asks again. Keys are
	// sorted so a client that regenerates the same call in another key order still matches.
	public static string Fingerprint(RequestContext<CallToolRequestParams> context)
	{
		using var buffer = new MemoryStream();
		using (var writer = new Utf8JsonWriter(buffer))
		{
			writer.WriteStartObject();
			var arguments = context.Params?.Arguments ?? new Dictionary<string, JsonElement>();
			foreach (var (name, value) in arguments.OrderBy(argument => argument.Key, StringComparer.Ordinal))
			{
				writer.WritePropertyName(name);
				WriteCanonical(writer, value);
			}

			writer.WriteEndObject();
		}

		byte[] call = [..Encoding.UTF8.GetBytes(context.Params?.Name + "\n"), ..buffer.ToArray()];
		return Convert.ToHexStringLower(SHA256.HashData(call));
	}

	private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
	{
		switch (value.ValueKind)
		{
			case JsonValueKind.Object:
				writer.WriteStartObject();
				var properties = value.EnumerateObject()
					.OrderBy(property => property.Name, StringComparer.Ordinal);
				foreach (var property in properties)
				{
					writer.WritePropertyName(property.Name);
					WriteCanonical(writer, property.Value);
				}

				writer.WriteEndObject();
				break;
			case JsonValueKind.Array:
				writer.WriteStartArray();
				foreach (var item in value.EnumerateArray())
				{
					WriteCanonical(writer, item);
				}

				writer.WriteEndArray();
				break;
			default:
				value.WriteTo(writer);
				break;
		}
	}
}
