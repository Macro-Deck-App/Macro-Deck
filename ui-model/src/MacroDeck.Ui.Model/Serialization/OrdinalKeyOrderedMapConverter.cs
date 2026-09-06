using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacroDeck.Ui.Model.Serialization;

/// <summary>
/// Writes every <c>IReadOnlyDictionary&lt;string, T&gt;</c> map shape in this model with its keys in
/// <see cref="StringComparer.Ordinal" /> ascending order, regardless of insertion order or the
/// concrete collection type behind the interface. <see cref="Utf8JsonWriter" /> otherwise writes a
/// dictionary in enumeration order, so the same logical tree built two ways would produce different
/// canonical bytes without this converter.
///
/// <para>
/// <see cref="MapConverter{T}.Read" /> is hand-written rather than delegating to
/// <c>JsonSerializer.Deserialize&lt;IReadOnlyDictionary&lt;string, T&gt;&gt;</c>: that call would
/// re-enter this same converter for the same type and recurse forever.
/// </para>
/// </summary>
internal sealed class OrdinalKeyOrderedMapConverter : JsonConverterFactory
{
	public override bool CanConvert(Type typeToConvert)
	{
		if (!typeToConvert.IsGenericType)
		{
			return false;
		}

		return typeToConvert.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>) &&
			typeToConvert.GetGenericArguments()[0] == typeof(string);
	}

	public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
	{
		var valueType = typeToConvert.GetGenericArguments()[1];
		var converterType = typeof(MapConverter<>).MakeGenericType(valueType);
		return (JsonConverter)Activator.CreateInstance(converterType)!;
	}

	private sealed class MapConverter<T> : JsonConverter<IReadOnlyDictionary<string, T>>
	{
		public override IReadOnlyDictionary<string, T> Read(
			ref Utf8JsonReader reader,
			Type typeToConvert,
			JsonSerializerOptions options)
		{
			if (reader.TokenType != JsonTokenType.StartObject)
			{
				throw new JsonException("Expected a JSON object.");
			}

			var map = new Dictionary<string, T>(StringComparer.Ordinal);

			while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
			{
				if (reader.TokenType != JsonTokenType.PropertyName)
				{
					throw new JsonException("Expected a property name.");
				}

				var key = reader.GetString()!;
				reader.Read();
				var value = JsonSerializer.Deserialize<T>(ref reader, options);

				// A JSON null under a key whose value type is a non-nullable reference type would put a
				// null into a map annotated as holding none - System.Text.Json does not enforce nullable
				// annotations - and every consumer would dereference it. Dropping the entry keeps the
				// wire contract non-fatal, exactly as a null collection normalizes to empty. A struct
				// value type such as JsonElement is never null here, so a null property value still
				// round-trips as an explicit null.
				if (value is null)
				{
					continue;
				}

				map[key] = value;
			}

			return map;
		}

		public override void Write(
			Utf8JsonWriter writer,
			IReadOnlyDictionary<string, T> value,
			JsonSerializerOptions options)
		{
			writer.WriteStartObject();

			foreach (var key in value.Keys.Order(StringComparer.Ordinal))
			{
				writer.WritePropertyName(key);
				JsonSerializer.Serialize(writer, value[key], options);
			}

			writer.WriteEndObject();
		}
	}
}
