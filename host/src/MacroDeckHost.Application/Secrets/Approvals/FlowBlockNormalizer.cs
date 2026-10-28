using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Integrations.ConfigFlow;

namespace MacroDeckHost.Application.Secrets.Approvals;

// The comparison form must equal what the desktop editor writes back for an untouched block
// (hydrateBlockParameters and normalizeLegacyParamTypes in ui/runtime action-flow.util.ts).
public static class FlowBlockNormalizer
{
	public static string Normalize(FlowBlockContent block, IActionDefinition? action, ISecretLineage? lineage)
	{
		using var buffer = new MemoryStream();
		using (var writer = new Utf8JsonWriter(buffer))
		{
			writer.WriteStartObject();
			writer.WriteString("action", $"{block.IntegrationId}.{block.ActionId}");
			writer.WritePropertyName("parameters");
			writer.WriteStartObject();

			var stored = new Dictionary<string, FlowBlockParameter>(StringComparer.Ordinal);
			foreach (var parameter in block.Parameters)
			{
				stored.TryAdd(parameter.Name, parameter);
			}

			var entries = new SortedDictionary<string, Action>(StringComparer.Ordinal);
			var declared = DeclaredSecretWalker.Index(action?.Parameters);
			foreach (var definition in declared.Values)
			{
				var value = stored.TryGetValue(definition.Name, out var parameter)
					? parameter.Value
					: ToElement(ActionParameterControlTypes.EditorDefault(definition));
				entries["d:" + definition.Name] = () => WriteCanonical(writer, value, lineage);
			}

			foreach (var parameter in stored.Values.Where(parameter => !declared.ContainsKey(parameter.Name)))
			{
				entries["u:" + parameter.Name] = () =>
				{
					writer.WriteStartObject();
					writer.WriteString("type", StoredType(parameter.Type));
					writer.WritePropertyName("value");
					WriteCanonical(writer, parameter.Value, lineage);
					writer.WriteEndObject();
				};
			}

			foreach (var (key, write) in entries)
			{
				writer.WritePropertyName(key);
				write();
			}

			writer.WriteEndObject();
			writer.WriteEndObject();
		}

		return Encoding.UTF8.GetString(buffer.ToArray());
	}

	public static string Fingerprint(string normalized)
	{
		return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
	}

	private static string StoredType(string type)
	{
		var lowered = type.ToLowerInvariant();
		return lowered == "select" ? "choice" : lowered;
	}

	private static JsonElement ToElement(JsonNode? node)
	{
		return node is null
			? JsonSerializer.SerializeToElement<object?>(null)
			: JsonSerializer.SerializeToElement(node);
	}

	private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value, ISecretLineage? lineage)
	{
		switch (value.ValueKind)
		{
			case JsonValueKind.Undefined:
				writer.WriteNullValue();
				break;
			case JsonValueKind.Object when SecretReferenceJson.TryGet(value, out var secretId):
				writer.WriteStartObject();
				writer.WriteString(SecretReferenceJson.PropertyName, (lineage?.Root(secretId) ?? secretId).ToString());
				writer.WriteEndObject();
				break;
			case JsonValueKind.Object:
				writer.WriteStartObject();
				foreach (var property in value.EnumerateObject()
					.OrderBy(property => property.Name, StringComparer.Ordinal))
				{
					writer.WritePropertyName(property.Name);
					WriteCanonical(writer, property.Value, lineage);
				}

				writer.WriteEndObject();
				break;
			case JsonValueKind.Array:
				writer.WriteStartArray();
				foreach (var item in value.EnumerateArray())
				{
					WriteCanonical(writer, item, lineage);
				}

				writer.WriteEndArray();
				break;
			default:
				value.WriteTo(writer);
				break;
		}
	}
}
