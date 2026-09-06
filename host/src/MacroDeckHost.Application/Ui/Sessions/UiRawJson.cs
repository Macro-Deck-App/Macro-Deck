using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacroDeckHost.Application.Ui.Sessions;

// A UI tree or patch crosses the host without ever being bound to UiTree or UiPatch. A
// deserialize-and-reserialize round trip would drop unknown members, reorder map keys and renormalise
// numbers, so the bytes a client applies would no longer be the bytes the provider signed off on.
[JsonConverter(typeof(UiRawJsonConverter))]
public readonly struct UiRawJson
{
	public UiRawJson(ReadOnlyMemory<byte> utf8)
	{
		Utf8 = utf8;
	}

	public ReadOnlyMemory<byte> Utf8 { get; }

	public bool IsEmpty => Utf8.IsEmpty;

	// Captures the element's own source text.
	// Deliberately not WriteTo: a writer pass re-escapes strings, drops the duplicate members and
	// insignificant whitespace a provider's bytes may contain, and renormalises numbers that do not fit
	// a double. Any of those would mean the client applies different bytes from the ones the
	// provider produced.
	public static UiRawJson FromElement(JsonElement element)
		=> new(JsonMarshal.GetRawUtf8Value(element).ToArray());

	public static UiRawJson FromUtf8(ReadOnlyMemory<byte> utf8) => new(utf8);

	// Re-parses the payload. Only for a boundary that insists on a JsonElement;
	// a relay path uses Utf8.
	public JsonElement ToElement()
	{
		if (IsEmpty)
		{
			return default;
		}

		var reader = new Utf8JsonReader(Utf8.Span);
		using var document = JsonDocument.ParseValue(ref reader);
		return document.RootElement.Clone();
	}

	public override string ToString() => Encoding.UTF8.GetString(Utf8.Span);
}

internal sealed class UiRawJsonConverter : JsonConverter<UiRawJson>
{
	public override UiRawJson Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		using var document = JsonDocument.ParseValue(ref reader);
		return UiRawJson.FromElement(document.RootElement);
	}

	public override void Write(Utf8JsonWriter writer, UiRawJson value, JsonSerializerOptions options)
	{
		if (value.IsEmpty)
		{
			writer.WriteNullValue();
			return;
		}

		// The bytes were validated on the way in; re-validating here would parse the payload a second
		// time on the hot path for no additional guarantee.
		writer.WriteRawValue(value.Utf8.Span, skipInputValidation: true);
	}
}
