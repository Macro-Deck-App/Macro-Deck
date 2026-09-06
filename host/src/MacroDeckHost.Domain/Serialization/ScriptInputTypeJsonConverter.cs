using System.Text.Json;
using System.Text.Json.Serialization;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Domain.Serialization;

/// <summary>
/// Writes a script input's type as the lowercase spelling the clients are written against
/// ("text", "numeric", "boolean") rather than the enum's own casing, and reads any casing back.
/// </summary>
public sealed class ScriptInputTypeJsonConverter : JsonConverter<ScriptInputType>
{
	public override ScriptInputType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.Number)
		{
			var numeric = reader.GetInt32();
			return Enum.IsDefined(typeof(ScriptInputType), numeric)
				? (ScriptInputType)numeric
				: throw new JsonException($"'{numeric}' is not a script input type.");
		}

		if (reader.TokenType != JsonTokenType.String)
		{
			throw new JsonException($"Expected a script input type, found {reader.TokenType}.");
		}

		var name = reader.GetString();
		return Enum.TryParse<ScriptInputType>(name, ignoreCase: true, out var type)
			? type
			: throw new JsonException($"'{name}' is not a script input type.");
	}

	public override void Write(Utf8JsonWriter writer, ScriptInputType value, JsonSerializerOptions options)
	{
		ArgumentNullException.ThrowIfNull(writer);

		writer.WriteStringValue(value.ToString().ToLowerInvariant());
	}
}
