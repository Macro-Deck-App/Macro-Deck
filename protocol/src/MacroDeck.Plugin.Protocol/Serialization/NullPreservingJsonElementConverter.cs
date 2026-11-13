using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacroDeck.Plugin.Protocol.Serialization;

// The framework reads a JSON null into a nullable struct as C# null, which makes an explicit null payload
// indistinguishable from an absent one. This keeps it a Null element.
internal sealed class NullPreservingJsonElementConverter : JsonConverter<JsonElement?>
{
	public override bool HandleNull => true;

	public override JsonElement? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		return JsonElement.ParseValue(ref reader);
	}

	public override void Write(Utf8JsonWriter writer, JsonElement? value, JsonSerializerOptions options)
	{
		if (value is not { } element)
		{
			writer.WriteNullValue();
			return;
		}

		var elementConverter = (JsonConverter<JsonElement>)options.GetConverter(typeof(JsonElement));
		elementConverter.Write(writer, element, options);
	}
}
