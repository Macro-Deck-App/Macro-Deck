using System.Text.Json;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Application.Ui.Sessions;

internal static class ReportedParameterValueFilter
{
	public static Dictionary<string, JsonElement> Filter(
		IReadOnlyList<ActionParameter> definitions,
		IReadOnlyDictionary<string, JsonElement> values,
		out List<string> dropped)
	{
		var byName = new Dictionary<string, ActionParameter>(StringComparer.Ordinal);
		foreach (var definition in definitions)
		{
			byName[definition.Name] = definition;
		}

		var accepted = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
		dropped = [];

		foreach (var (name, value) in values)
		{
			if (byName.TryGetValue(name, out var definition) && Accepts(definition.Type, value))
			{
				accepted[name] = value;
			}
			else
			{
				dropped.Add(name);
			}
		}

		return accepted;
	}

	// Secrets never travel this way: the client masks them and only a user's own entry may set one.
	private static bool Accepts(ActionParameterType type, JsonElement value)
	{
		if (type is ActionParameterType.Secret or ActionParameterType.Password)
		{
			return false;
		}

		if (value.ValueKind == JsonValueKind.Null)
		{
			return true;
		}

		return type switch
		{
			ActionParameterType.KeyValue => IsStringMap(value),
			ActionParameterType.Object => value.ValueKind == JsonValueKind.Object,
			ActionParameterType.MultiSelect or ActionParameterType.Array => value.ValueKind == JsonValueKind.Array,
			ActionParameterType.String
				or ActionParameterType.Number
				or ActionParameterType.Boolean
				or ActionParameterType.Choice
				or ActionParameterType.DynamicChoice
				or ActionParameterType.Autocomplete
				or ActionParameterType.Color
				or ActionParameterType.Url
				or ActionParameterType.IpAddress => IsScalar(value),
			_ => true
		};
	}

	private static bool IsScalar(JsonElement value)
	{
		return value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array);
	}

	private static bool IsStringMap(JsonElement value)
	{
		if (value.ValueKind != JsonValueKind.Object)
		{
			return false;
		}

		foreach (var property in value.EnumerateObject())
		{
			if (property.Value.ValueKind != JsonValueKind.String)
			{
				return false;
			}
		}

		return true;
	}
}
