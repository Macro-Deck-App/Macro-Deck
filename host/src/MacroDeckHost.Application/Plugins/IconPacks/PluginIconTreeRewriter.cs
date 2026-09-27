using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Domain.Widgets;

namespace MacroDeckHost.Application.Plugins.IconPacks;

public static class PluginIconTreeRewriter
{
	private static readonly byte[] _marker = Encoding.UTF8.GetBytes(PluginIconReferences.Type);

	// The rewritten bytes are relayed as JSON only, so escaping them more than the plugin did would only
	// grow the payload towards its size limit.
	private static readonly JsonSerializerOptions _writeOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

	public static UiRawJson Rewrite(string pluginId, UiRawJson payload, IPluginIconResolver resolver)
	{
		if (payload.IsEmpty || payload.Utf8.Span.IndexOf(_marker) < 0)
		{
			return payload;
		}

		try
		{
			var root = Visit(JsonNode.Parse(payload.Utf8.Span), pluginId, resolver);
			return UiRawJson.FromUtf8(Encoding.UTF8.GetBytes(root?.ToJsonString(_writeOptions) ?? "null"));
		}
		catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
		{
			return payload;
		}
	}

	private static JsonNode? Visit(JsonNode? node, string pluginId, IPluginIconResolver resolver)
	{
		switch (node)
		{
			case JsonObject obj when IsPluginIconReference(obj, out var reference):
				return resolver.Resolve(pluginId, reference) is { } icon
					? new JsonObject
					{
						["type"] = WidgetIconReference.IconPackType,
						["reference"] = icon.Id.ToString()
					}
					: null;

			case JsonObject obj:
				foreach (var name in obj.Select(property => property.Key).ToList())
				{
					var child = obj[name];
					var replaced = Visit(child, pluginId, resolver);
					if (!ReferenceEquals(child, replaced))
					{
						obj[name] = replaced;
					}
				}

				return obj;

			case JsonArray array:
				for (var index = 0; index < array.Count; index++)
				{
					var child = array[index];
					var replaced = Visit(child, pluginId, resolver);
					if (!ReferenceEquals(child, replaced))
					{
						array[index] = replaced;
					}
				}

				return array;

			default:
				return node;
		}
	}

	private static bool IsPluginIconReference(JsonObject obj, out string reference)
	{
		reference = string.Empty;
		if (obj.Count != 2 ||
			obj["type"] is not JsonValue type ||
			!type.TryGetValue<string>(out var typeValue) ||
			!string.Equals(typeValue, PluginIconReferences.Type, StringComparison.Ordinal) ||
			obj["reference"] is not JsonValue value ||
			!value.TryGetValue<string>(out var referenceValue))
		{
			return false;
		}

		reference = referenceValue;
		return true;
	}
}
