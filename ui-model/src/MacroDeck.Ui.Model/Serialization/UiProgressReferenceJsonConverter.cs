using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MacroDeck.Ui.Model.References;

namespace MacroDeck.Ui.Model.Serialization;

/// <summary>
/// Writes a <see cref="UiProgressReference" /> as <c>{"$progress":{…}}</c>. The marker member is what
/// lets a reader tell a reader-resolved reference from an ordinary property value by shape alone, without a
/// schema - the same job <see cref="UiTimeReferenceJsonConverter.Marker" /> does, and the reason a
/// second marker could be added to one property in the first place.
/// </summary>
public sealed class UiProgressReferenceJsonConverter : JsonConverter<UiProgressReference>
{
	/// <summary>The single member marking the progress-reference shape.</summary>
	public const string Marker = "$progress";

	private const string PositionProperty = "positionMs";
	private const string DurationProperty = "durationMs";
	private const string RateProperty = "rate";
	private const string AnchorProperty = "anchor";

	// UTC, milliseconds, no offset spelling to choose between: an anchor is an instant, and two producers
	// that wrote the same instant differently would produce different canonical bytes for one tree.
	private const string AnchorFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";

	/// <inheritdoc />
	public override UiProgressReference? Read(ref Utf8JsonReader reader,
		Type typeToConvert,
		JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.Null)
		{
			return null;
		}

		if (reader.TokenType != JsonTokenType.StartObject)
		{
			throw new JsonException("A progress reference must be an object.");
		}

		using var document = JsonDocument.ParseValue(ref reader);

		if (!document.RootElement.TryGetProperty(Marker, out var marker) ||
			marker.ValueKind != JsonValueKind.Object)
		{
			throw new JsonException($"A progress reference must carry an object '{Marker}' member.");
		}

		foreach (var member in marker.EnumerateObject())
		{
			if (member.Name is not (PositionProperty or DurationProperty or RateProperty or AnchorProperty))
			{
				throw new JsonException($"A '{Marker}' member does not define '{member.Name}'.");
			}
		}

		return new UiProgressReference
		{
			PositionMs = ReadRequiredInt64(marker, PositionProperty),
			Anchor = ReadRequiredAnchor(marker),
			// Tolerant here, strict in Write, exactly as the time reference is: an explicit null and an
			// absent member both mean the same thing, so a producer that spells it out is understood and
			// the spelling never leaves this process.
			DurationMs = ReadOptionalInt64(marker, DurationProperty),
			Rate = ReadOptionalDouble(marker, RateProperty),
		};
	}

	/// <inheritdoc />
	public override void Write(Utf8JsonWriter writer,
		UiProgressReference value,
		JsonSerializerOptions options)
	{
		ArgumentNullException.ThrowIfNull(writer);
		ArgumentNullException.ThrowIfNull(value);

		writer.WriteStartObject();
		writer.WriteStartObject(Marker);

		writer.WriteNumber(PositionProperty, value.PositionMs);

		// Omitted rather than written null: the model reads an explicit null as "explicitly null", and both
		// of these carry a stated meaning when absent - unknown length, and normal speed.
		if (value.DurationMs is { } duration)
		{
			writer.WriteNumber(DurationProperty, duration);
		}

		if (value.Rate is { } rate)
		{
			writer.WriteNumber(RateProperty, rate);
		}

		writer.WriteString(AnchorProperty,
			value.Anchor.ToUniversalTime().ToString(AnchorFormat, CultureInfo.InvariantCulture));

		writer.WriteEndObject();
		writer.WriteEndObject();
	}

	private static long ReadRequiredInt64(JsonElement marker, string name)
	{
		if (!marker.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Number)
		{
			throw new JsonException($"A '{Marker}' member must carry a numeric '{name}'.");
		}

		return element.GetInt64();
	}

	private static long? ReadOptionalInt64(JsonElement marker, string name)
	{
		if (!marker.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
		{
			return null;
		}

		if (element.ValueKind != JsonValueKind.Number)
		{
			throw new JsonException($"A '{Marker}' member's '{name}' must be a number.");
		}

		return element.GetInt64();
	}

	private static double? ReadOptionalDouble(JsonElement marker, string name)
	{
		if (!marker.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
		{
			return null;
		}

		if (element.ValueKind != JsonValueKind.Number)
		{
			throw new JsonException($"A '{Marker}' member's '{name}' must be a number.");
		}

		return element.GetDouble();
	}

	private static DateTimeOffset ReadRequiredAnchor(JsonElement marker)
	{
		if (!marker.TryGetProperty(AnchorProperty, out var element) ||
			element.ValueKind != JsonValueKind.String)
		{
			throw new JsonException($"A '{Marker}' member must carry a string '{AnchorProperty}'.");
		}

		if (!element.TryGetDateTimeOffset(out var anchor))
		{
			throw new JsonException($"A '{Marker}' member's '{AnchorProperty}' must be an ISO-8601 instant.");
		}

		return anchor;
	}
}
