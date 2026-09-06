using System.Globalization;
using System.Text.Json;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Scripts;

public readonly record struct ScriptInputBinding(
	IReadOnlyDictionary<string, object?> Values,
	IReadOnlyCollection<string> AppliedInputs,
	string? ErrorCode,
	string? ErrorMessage)
{
	public bool Success => ErrorCode is null;
}

/// <summary>
/// Turns the values a caller supplied into the overlay a run reads as <c>vars.&lt;name&gt;</c>: undeclared
/// names are dropped, a missing value falls back to the declaration's default, and everything else is
/// coerced to the declared type.
/// </summary>
public static class ScriptInputBinder
{
	public static ScriptInputBinding Bind(
		IReadOnlyList<ScriptInput>? declarations,
		IReadOnlyDictionary<string, object?>? supplied)
	{
		var values = new Dictionary<string, object?>(StringComparer.Ordinal);
		var applied = new List<string>();
		if (declarations is not { Count: > 0 })
		{
			return new ScriptInputBinding(values, applied, null, null);
		}

		foreach (var declaration in declarations)
		{
			var name = declaration.Name;
			if (string.IsNullOrEmpty(name))
			{
				continue;
			}

			// A supplied value wins even when it is empty, zero or false, so a caller can deliberately
			// override a non-falsy default.
			if (supplied is not null && supplied.TryGetValue(name, out var raw))
			{
				if (!TryCoerce(declaration, Normalize(raw), out var coerced, out var display))
				{
					return Failed(ActionExecutionErrorCodes.ScriptInputInvalid,
						$"'{display}' is not a valid value for the script input '{name}'.");
				}

				values[name] = coerced;
				applied.Add(name);
				continue;
			}

			if (declaration.DefaultValue is not null)
			{
				if (!TryCoerce(declaration, declaration.DefaultValue, out var fallback, out var display))
				{
					return Failed(ActionExecutionErrorCodes.ScriptInputInvalid,
						$"'{display}' is not a valid default for the script input '{name}'.");
				}

				values[name] = fallback;
				continue;
			}

			if (declaration.Required)
			{
				return Failed(ActionExecutionErrorCodes.ScriptInputMissing,
					$"The script input '{name}' is required but no value was supplied.");
			}
		}

		return new ScriptInputBinding(values, applied, null, null);
	}

	private static ScriptInputBinding Failed(string code, string message)
		=> new(new Dictionary<string, object?>(StringComparer.Ordinal), [], code, message);

	// Values arriving over HTTP or the plugin protocol are JsonElements; flatten them to plain CLR values
	// so coercion and the resulting overlay never leak a serializer type into a script.
	private static object? Normalize(object? raw) => raw switch
	{
		JsonElement element => element.ValueKind switch
		{
			JsonValueKind.String => element.GetString(),
			JsonValueKind.Number => element.GetDouble(),
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			JsonValueKind.Null or JsonValueKind.Undefined => null,
			_ => element.GetRawText()
		},
		_ => raw
	};

	private static bool TryCoerce(
		ScriptInput declaration,
		object? raw,
		out object? value,
		out string display)
	{
		display = Stringify(raw);

		switch (declaration.Type)
		{
			case ScriptInputType.Numeric:
				if (raw is double or float or int or long or decimal)
				{
					value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
					return true;
				}

				if (double.TryParse(display, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
				{
					value = number;
					return true;
				}

				value = null;
				return false;

			case ScriptInputType.Boolean:
				if (raw is bool flag)
				{
					value = flag;
					return true;
				}

				switch (display.Trim().ToLowerInvariant())
				{
					case "true" or "1":
						value = true;
						return true;
					case "false" or "0":
						value = false;
						return true;
					default:
						value = null;
						return false;
				}

			case ScriptInputType.Text:
			default:
				value = display;
				return true;
		}
	}

	private static string Stringify(object? value) => value switch
	{
		null => string.Empty,
		bool b => b ? "true" : "false",
		string s => s,
		IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
		_ => value.ToString() ?? string.Empty
	};
}
