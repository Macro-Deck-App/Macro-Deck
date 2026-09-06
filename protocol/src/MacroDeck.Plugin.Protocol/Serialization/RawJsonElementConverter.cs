using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacroDeck.Plugin.Protocol.Serialization;

/// <summary>
/// Writes a <see cref="JsonElement" /> as the bytes it was parsed from rather than re-encoding it.
/// </summary>
/// <remarks>
/// Every <see cref="JsonElement" />-typed member on this protocol is an opaque pass-through: a UI tree
/// or patch, a surface attribute bag, a capability's own arguments. The framework's own element writer
/// is <c>WriteTo</c>, which unescapes <c>é</c> into its UTF-8 bytes, re-escapes <c>&lt;</c> as
/// <c><</c> and drops insignificant whitespace - so a payload that crossed three
/// serialize-into-a-parent hops (arguments into a payload, payload into an envelope, envelope onto the
/// socket) reached the peer as different bytes from the ones the producer signed off on. Reading is
/// unchanged: it is the write side alone that re-encoded.
/// </remarks>
internal sealed class RawJsonElementConverter : JsonConverter<JsonElement>
{
	public override JsonElement Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		=> JsonElement.ParseValue(ref reader);

	public override void Write(Utf8JsonWriter writer, JsonElement value, JsonSerializerOptions options)
	{
		if (value.ValueKind == JsonValueKind.Undefined)
		{
			// Not writable as raw text, and deliberately left to throw exactly as it did before this
			// converter existed rather than being silently turned into a null.
			value.WriteTo(writer);
			return;
		}

		// The value already parsed as JSON to become a JsonElement, so validating it a second time on
		// the hot path buys nothing - but only because this converter is registered on
		// PluginProtocolJson alone, which parses with ReadCommentHandling.Disallow and
		// AllowTrailingCommas = false. GetRawUtf8Value returns the element's own source text verbatim,
		// so an element that came from a lenient document would carry its comments and trailing commas
		// into the envelope and put invalid JSON on the wire. Registering this converter on any options
		// instance that relaxes either setting - PluginManifestJson does relax one - breaks that
		// invariant. See RawJsonElementConverterTests.
		writer.WriteRawValue(JsonMarshal.GetRawUtf8Value(value), skipInputValidation: true);
	}
}
