using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacroDeck.Sdk.Scripts;

/// <summary>The type a script input's value is coerced to before the script reads it.</summary>
[JsonConverter(typeof(ScriptInputTypeConverter))]
public enum ScriptInputType
{
	Text = 0,
	Numeric = 1,
	Boolean = 2
}

/// <summary>
/// One value a script declares that its caller supplies. The script reads it as <c>vars.&lt;name&gt;</c>,
/// where it shadows a global variable of the same name for the duration of that run and cannot be written
/// to.
/// </summary>
public sealed class ScriptInput
{
	/// <summary>Matches <c>^[a-z][a-z0-9_]*$</c>; this is the name the script reads under <c>vars.</c>.</summary>
	public string Name { get; init; } = string.Empty;

	public ScriptInputType Type { get; init; }

	/// <summary>Display name for an input form. Empty means the caller shows <see cref="Name" />.</summary>
	public string? Label { get; init; }

	public string? Description { get; init; }

	/// <summary>
	/// A required input with neither a supplied value nor a <see cref="DefaultValue" /> fails the run
	/// before any block executes.
	/// </summary>
	public bool Required { get; init; }

	/// <summary>Used when the caller supplies nothing. A supplied value always wins, including a falsy one.</summary>
	public string? DefaultValue { get; init; }
}

internal sealed class ScriptInputTypeConverter : JsonConverter<ScriptInputType>
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
