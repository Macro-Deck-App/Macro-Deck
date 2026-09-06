using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacroDeck.Localization.Serialization;

/// <summary>
/// Writes literal <see cref="LocalizedText" /> as a bare JSON string and a localized one as
/// <c>{"$localized":{…}}</c>. The two shapes are distinguishable by token type alone, so a reader never
/// needs a schema to tell them apart, and a producer that has always written plain strings keeps
/// producing byte-identical output.
/// </summary>
public sealed class LocalizedTextJsonConverter : JsonConverter<LocalizedText>
{
	/// <summary>The single member marking the localized object shape.</summary>
	public const string Marker = "$localized";

	private const string ScopeProperty = "scope";
	private const string KeyProperty = "key";
	private const string ArgumentsProperty = "arguments";

	/// <inheritdoc />
	public override LocalizedText Read(ref Utf8JsonReader reader,
		Type typeToConvert,
		JsonSerializerOptions options)
		=> reader.TokenType switch
		{
			JsonTokenType.Null => default,
			JsonTokenType.String => LocalizedText.FromLiteral(reader.GetString()),
			JsonTokenType.StartObject => ReadLocalized(ref reader),
			_ => throw new JsonException("Localized text must be a string or a localization object."),
		};

	/// <inheritdoc />
	public override void Write(Utf8JsonWriter writer, LocalizedText value, JsonSerializerOptions options)
	{
		if (value.Localized is not { } localized)
		{
			if (value.Literal is null)
			{
				writer.WriteNullValue();
			}
			else
			{
				writer.WriteStringValue(value.Literal);
			}

			return;
		}

		writer.WriteStartObject();
		writer.WriteStartObject(Marker);
		writer.WriteString(ScopeProperty, localized.Key.Scope);
		writer.WriteString(KeyProperty, localized.Key.Name);

		if (localized.Arguments.Count > 0)
		{
			writer.WriteStartObject(ArgumentsProperty);

			// Ordinal ordering so the same reference serializes to the same bytes every time - the UI
			// model diffs property values by their canonical raw text.
			foreach (var argument in localized.Arguments.OrderBy(a => a.Key, StringComparer.Ordinal))
			{
				writer.WritePropertyName(argument.Key);
				JsonSerializer.Serialize(writer, argument.Value, options);
			}

			writer.WriteEndObject();
		}

		writer.WriteEndObject();
		writer.WriteEndObject();
	}

	private static LocalizedText ReadLocalized(ref Utf8JsonReader reader)
	{
		using var document = JsonDocument.ParseValue(ref reader);

		if (!document.RootElement.TryGetProperty(Marker, out var marker))
		{
			throw new JsonException($"A localized text object must carry a '{Marker}' member.");
		}

		if (!marker.TryGetProperty(ScopeProperty, out var scope) ||
			!marker.TryGetProperty(KeyProperty, out var key) ||
			scope.ValueKind != JsonValueKind.String ||
			key.ValueKind != JsonValueKind.String)
		{
			throw new JsonException($"A '{Marker}' member needs string '{ScopeProperty}' and '{KeyProperty}'.");
		}

		var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);

		if (marker.TryGetProperty(ArgumentsProperty, out var declared) &&
			declared.ValueKind == JsonValueKind.Object)
		{
			foreach (var argument in declared.EnumerateObject())
			{
				arguments[argument.Name] = ReadArgument(argument.Value);
			}
		}

		return LocalizedText.FromLocalized(
			new LocalizedString(new LocalizationKey(scope.GetString()!, key.GetString()!), arguments));
	}

	private static object? ReadArgument(JsonElement value) => value.ValueKind switch
	{
		JsonValueKind.String => value.GetString(),
		JsonValueKind.True => true,
		JsonValueKind.False => false,
		JsonValueKind.Number => value.TryGetInt64(out var integer) ? integer : value.GetDouble(),
		_ => null,
	};
}
