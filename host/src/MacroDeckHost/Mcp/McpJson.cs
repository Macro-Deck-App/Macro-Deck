using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace MacroDeckHost.Mcp;

public static class McpJson
{
	public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
	{
		TypeInfoResolver = new DefaultJsonTypeInfoResolver()
	};

	public static JsonElement ToElement<T>(T value)
	{
		return JsonSerializer.SerializeToElement(value, Options);
	}

	public static JsonElement? ParseOrNull(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return null;
		}

		try
		{
			using var document = JsonDocument.Parse(json);
			return document.RootElement.Clone();
		}
		catch (JsonException)
		{
			return JsonSerializer.SerializeToElement(json, Options);
		}
	}
}
