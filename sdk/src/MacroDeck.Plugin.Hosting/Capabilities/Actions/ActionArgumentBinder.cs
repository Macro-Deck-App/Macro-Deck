using System.Globalization;
using System.Text.Json;
using MacroDeck.Sdk.Actions;

namespace MacroDeck.Plugin.Hosting.Capabilities.Actions;

/// <summary>
/// Turns the JSON arguments of a <c>capability.invoke</c> into the parameter dictionary an
/// <see cref="IActionExecutor" /> expects.
///
/// <para>
/// The declared <see cref="ActionParameterType" /> decides the CLR shape, not the JSON token: an
/// action declaring a number must get a number even when the flow stored "50" as a string, because
/// in-process every executor already relies on that. Anything that cannot be converted is passed
/// through as the raw value rather than dropped, so an executor's own validation reports the problem
/// with the detail it has and this layer does not guess.
/// </para>
/// </summary>
internal static class ActionArgumentBinder
{
	public static IReadOnlyDictionary<string, object> Bind(
		IReadOnlyList<ActionParameter> parameters,
		JsonElement? arguments)
	{
		var bound = new Dictionary<string, object>(StringComparer.Ordinal);

		if (arguments is not { ValueKind: JsonValueKind.Object } element)
		{
			return bound;
		}

		var declared = parameters.ToDictionary(parameter => parameter.Name, StringComparer.Ordinal);

		foreach (var property in element.EnumerateObject())
		{
			var type = declared.TryGetValue(property.Name, out var parameter)
				? parameter.Type
				: (ActionParameterType?)null;
			var value = Convert(property.Value, type);

			if (value is not null)
			{
				bound[property.Name] = value;
			}
		}

		return bound;
	}

	private static object? Convert(JsonElement value, ActionParameterType? type)
		=> type switch
		{
			ActionParameterType.Number or ActionParameterType.Duration => AsNumber(value),
			ActionParameterType.Boolean => AsBoolean(value),
			_ => AsDeclaredShape(value)
		};

	private static object? AsNumber(JsonElement value)
		=> value.ValueKind switch
		{
			JsonValueKind.Number when value.TryGetDouble(out var number) => number,
			JsonValueKind.String when double.TryParse(value.GetString(),
				NumberStyles.Float,
				CultureInfo.InvariantCulture,
				out var parsed) => parsed,
			_ => AsDeclaredShape(value)
		};

	private static object? AsBoolean(JsonElement value)
		=> value.ValueKind switch
		{
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
			_ => AsDeclaredShape(value)
		};

	/// <summary>
	/// The value as the JSON says it is. Objects and arrays stay <see cref="JsonElement" />: they are
	/// the shapes an action reads with its own schema, and flattening them here would lose exactly the
	/// structure the action wants.
	/// </summary>
	private static object? AsDeclaredShape(JsonElement value)
		=> value.ValueKind switch
		{
			JsonValueKind.String => value.GetString(),
			JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
			JsonValueKind.Number when value.TryGetDouble(out var number) => number,
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			JsonValueKind.Null or JsonValueKind.Undefined => null,
			_ => value.Clone()
		};
}
