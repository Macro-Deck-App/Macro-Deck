using System.Text.Json;
using System.Text.Json.Serialization;
using MacroDeck.Ui.Model.References;

namespace MacroDeck.Ui.Model.Serialization;

/// <summary>
/// Writes a <see cref="UiTimeReference" /> as <c>{"$time":{…}}</c>. The marker member is what
/// lets a reader tell a reader-resolved reference from an ordinary property value by shape alone,
/// without a schema, and what leaves room for a further marker on the same property later.
/// </summary>
public sealed class UiTimeReferenceJsonConverter : JsonConverter<UiTimeReference>
{
	/// <summary>The single member marking the time-reference shape.</summary>
	public const string Marker = "$time";

	private const string ZoneProperty = "zone";

	/// <inheritdoc />
	public override UiTimeReference? Read(ref Utf8JsonReader reader,
		Type typeToConvert,
		JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.Null)
		{
			return null;
		}

		if (reader.TokenType != JsonTokenType.StartObject)
		{
			throw new JsonException("A time reference must be an object.");
		}

		using var document = JsonDocument.ParseValue(ref reader);

		if (!document.RootElement.TryGetProperty(Marker, out var marker) ||
			marker.ValueKind != JsonValueKind.Object)
		{
			throw new JsonException($"A time reference must carry an object '{Marker}' member.");
		}

		foreach (var member in marker.EnumerateObject())
		{
			if (!string.Equals(member.Name, ZoneProperty, StringComparison.Ordinal))
			{
				throw new JsonException($"A '{Marker}' member does not define '{member.Name}'.");
			}
		}

		// Tolerant here, strict in Write: an explicit null and an absent member both mean the reader's own
		// zone, so a producer that spells it out is understood, and the spelling never leaves this process.
		if (!marker.TryGetProperty(ZoneProperty, out var zone) || zone.ValueKind == JsonValueKind.Null)
		{
			return new UiTimeReference();
		}

		if (zone.ValueKind != JsonValueKind.String)
		{
			throw new JsonException($"A '{Marker}' member's '{ZoneProperty}' must be a string.");
		}

		return new UiTimeReference { Zone = zone.GetString() };
	}

	/// <inheritdoc />
	public override void Write(Utf8JsonWriter writer, UiTimeReference value, JsonSerializerOptions options)
	{
		ArgumentNullException.ThrowIfNull(writer);
		ArgumentNullException.ThrowIfNull(value);

		writer.WriteStartObject();
		writer.WriteStartObject(Marker);

		// Omitted rather than written null: the model reads an explicit null as "explicitly null", and a
		// zone-less reference means the reader's own zone.
		if (value.Zone is not null)
		{
			writer.WriteString(ZoneProperty, value.Zone);
		}

		writer.WriteEndObject();
		writer.WriteEndObject();
	}
}
