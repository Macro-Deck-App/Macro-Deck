using System.Text.Json;
using System.Text.Json.Serialization;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Domain.Serialization;

/// <summary>
/// Reads every spelling <c>user-variables.json</c> has ever carried for a variable scope and writes the
/// current one. Files written before the widget scope was generalized store the name "ActionButton", and
/// older ones still store the raw numeric value, so both have to keep loading.
/// </summary>
public sealed class VariableScopeJsonConverter : JsonConverter<VariableScope>
{
	private const string _legacyWidgetName = "ActionButton";

	public override VariableScope Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.Number)
		{
			var numeric = reader.GetInt32();
			return Enum.IsDefined(typeof(VariableScope), numeric)
				? (VariableScope)numeric
				: throw new JsonException($"'{numeric}' is not a variable scope.");
		}

		if (reader.TokenType != JsonTokenType.String)
		{
			throw new JsonException($"Expected a variable scope, found {reader.TokenType}.");
		}

		var name = reader.GetString();

		if (string.Equals(name, _legacyWidgetName, StringComparison.OrdinalIgnoreCase))
		{
			return VariableScope.Widget;
		}

		return Enum.TryParse<VariableScope>(name, ignoreCase: true, out var scope)
			? scope
			: throw new JsonException($"'{name}' is not a variable scope.");
	}

	public override void Write(Utf8JsonWriter writer, VariableScope value, JsonSerializerOptions options)
	{
		ArgumentNullException.ThrowIfNull(writer);

		writer.WriteStringValue(value.ToString());
	}
}
