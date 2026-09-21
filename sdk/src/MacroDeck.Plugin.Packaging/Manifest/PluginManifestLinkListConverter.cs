using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacroDeck.Plugin.Packaging.Manifest;

// The reader must never fail on additionalLinks: a manifest that loads today has to keep loading
// (PluginManifestValidationLevel.Development), so every shape degrades instead of throwing.
internal sealed class PluginManifestLinkListConverter : JsonConverter<IReadOnlyList<PluginManifestLink>?>
{
	public override IReadOnlyList<PluginManifestLink>? Read(ref Utf8JsonReader reader,
		Type typeToConvert,
		JsonSerializerOptions options)
	{
		using var document = JsonDocument.ParseValue(ref reader);
		if (document.RootElement.ValueKind != JsonValueKind.Array)
		{
			return null;
		}

		return document.RootElement.EnumerateArray()
			.Select(entry => entry.ValueKind == JsonValueKind.Object
				? new PluginManifestLink
				{
					Type = PluginManifestLinks.StringProperty(entry, "type"),
					Url = PluginManifestLinks.StringProperty(entry, "url"),
					Label = PluginManifestLinks.StringProperty(entry, "label")
				}
				: new PluginManifestLink())
			.ToList();
	}

	// Null members are omitted whatever the options say: a "label": null on a standard type would make the
	// packed manifest fail the published schema.
	public override void Write(Utf8JsonWriter writer,
		IReadOnlyList<PluginManifestLink>? value,
		JsonSerializerOptions options)
	{
		if (value is null)
		{
			writer.WriteNullValue();
			return;
		}

		writer.WriteStartArray();
		foreach (var link in value)
		{
			writer.WriteStartObject();
			WriteIfPresent(writer, "type", link.Type);
			WriteIfPresent(writer, "url", link.Url);
			WriteIfPresent(writer, "label", link.Label);
			writer.WriteEndObject();
		}

		writer.WriteEndArray();
	}

	private static void WriteIfPresent(Utf8JsonWriter writer, string name, string? value)
	{
		if (value is not null)
		{
			writer.WriteString(name, value);
		}
	}
}
