using System.Globalization;
using System.Text.Json;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Variables;

public interface IActionConditionEvaluator
{
	EvaluatedSide ResolveSide(JsonElement value, VariableContext variables);

	bool Evaluate(JsonElement left, string? op, JsonElement right, VariableContext variables);

	bool EvaluateExpression(JsonElement expression,
		VariableContext variables,
		Action<ConditionLeafOutcome>? onLeaf = null);

	string? RenderTemplateString(string? raw, VariableContext variables);
}

public readonly record struct EvaluatedSide(object? Value, string Display);

public readonly record struct ConditionLeafOutcome(
	string Id,
	bool? Result,
	string LeftDisplay,
	string RightDisplay,
	string? Error);

public sealed class ActionConditionEvaluator : IActionConditionEvaluator
{
	private readonly IVariableTemplateRenderer _renderer;

	public ActionConditionEvaluator(IVariableTemplateRenderer renderer)
	{
		_renderer = renderer;
	}

	public EvaluatedSide ResolveSide(JsonElement value, VariableContext variables)
	{
		switch (value.ValueKind)
		{
			case JsonValueKind.Undefined:
			case JsonValueKind.Null:
				return new EvaluatedSide(null, string.Empty);

			case JsonValueKind.String:
			{
				var raw = value.GetString() ?? string.Empty;
				var rendered = RenderTemplateString(raw, variables) ?? string.Empty;
				return new EvaluatedSide(rendered, rendered);
			}

			case JsonValueKind.Number:
			{
				var number = value.TryGetInt64(out var i) ? (object)i : value.GetDouble();
				return new EvaluatedSide(number, FormatDisplay(number));
			}

			case JsonValueKind.True: return new EvaluatedSide(true, "true");
			case JsonValueKind.False: return new EvaluatedSide(false, "false");

			case JsonValueKind.Object:
			{
				var resolved = ResolveVariableReference(value, variables);
				if (resolved is not null)
				{
					return new EvaluatedSide(resolved, FormatDisplay(resolved));
				}

				// ResolveVariableReference returns null for two different reasons that must not be
				// treated alike: "this is a $var/$event reference and it did not resolve" (an unknown
				// name, an unavailable provider variable, an unknown event parameter) must read as
				// unresolvable, exactly like Null/Undefined above - not as the reference's own raw JSON
				// text. Falling through to raw text here made an unresolvable operand compare as if it
				// were the reference's literal source text, which starts with the '{' brace and therefore
				// sorted *greater* than any digit - turning a threshold condition like "brightness > 128"
				// true while brightness was unavailable, the opposite of what an unresolvable operand
				// must do. A JSON object that is not a reference at all (no $var/$event key) has nothing
				// else to fall back to, so its raw text is still the least-wrong display/comparison value.
				return IsVariableOrEventReference(value)
					? new EvaluatedSide(null, string.Empty)
					: new EvaluatedSide(value.GetRawText(), value.GetRawText());
			}

			default:
				return new EvaluatedSide(value.ToString(), value.ToString());
		}
	}

	public bool Evaluate(JsonElement left, string? op, JsonElement right, VariableContext variables)
	{
		var l = ResolveSide(left, variables);
		var r = ResolveSide(right, variables);
		return EvaluateOperator(l.Value, op ?? "==", r.Value);
	}

	public bool EvaluateExpression(
		JsonElement expression,
		VariableContext variables,
		Action<ConditionLeafOutcome>? onLeaf = null)
		=> EvaluateNode(expression, variables, onLeaf, shortCircuited: false);

	private bool EvaluateNode(
		JsonElement node,
		VariableContext variables,
		Action<ConditionLeafOutcome>? onLeaf,
		bool shortCircuited)
	{
		if (node.ValueKind != JsonValueKind.Object)
		{
			return false;
		}

		var kind = node.TryGetProperty("kind", out var kindElement) && kindElement.ValueKind == JsonValueKind.String
			? kindElement.GetString()
			: null;

		return kind switch
		{
			"compare" => EvaluateComparison(node, variables, onLeaf, shortCircuited),
			"and" => EvaluateLogical(node, variables, onLeaf, shortCircuited, isAnd: true),
			"or" => EvaluateLogical(node, variables, onLeaf, shortCircuited, isAnd: false),
			null when IsUntaggedComparison(node) => EvaluateComparison(node, variables, onLeaf, shortCircuited),
			_ => false
		};
	}

	private static bool IsUntaggedComparison(JsonElement node)
		=> node.TryGetProperty("operator", out _) ||
			node.TryGetProperty("left", out _) ||
			node.TryGetProperty("right", out _);

	private bool EvaluateComparison(
		JsonElement node,
		VariableContext variables,
		Action<ConditionLeafOutcome>? onLeaf,
		bool shortCircuited)
	{
		var id = node.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String
			? idElement.GetString() ?? string.Empty
			: string.Empty;

		var op = node.TryGetProperty("operator", out var opElement) && opElement.ValueKind == JsonValueKind.String
			? opElement.GetString() ?? "=="
			: "==";

		var leftValue = ExtractSideValue(node, "left");
		var rightValue = ExtractSideValue(node, "right");

		var leftDisplay = string.Empty;
		var rightDisplay = string.Empty;

		try
		{
			var leftSide = ResolveSide(leftValue, variables);
			leftDisplay = leftSide.Display;

			// A state operator has no right operand, so the side is not resolved at all: the leaf badge
			// cannot then show a stale value the verdict never depended on, and a leftover template is not
			// rendered for nothing.
			object? rightOperand = null;
			if (!IsStateOperator(op))
			{
				var rightSide = ResolveSide(rightValue, variables);
				rightDisplay = rightSide.Display;
				rightOperand = rightSide.Value;
			}

			var verdict = EvaluateOperator(leftSide.Value, op, rightOperand);
			Report(onLeaf, id, shortCircuited ? null : verdict, leftDisplay, rightDisplay, error: null);
			return verdict;
		}
		catch (Exception ex)
		{
			Report(onLeaf, id, shortCircuited ? null : false, leftDisplay, rightDisplay, ex.Message);
			return false;
		}
	}

	private bool EvaluateLogical(
		JsonElement node,
		VariableContext variables,
		Action<ConditionLeafOutcome>? onLeaf,
		bool shortCircuited,
		bool isAnd)
	{
		if (!node.TryGetProperty("operands", out var operands) || operands.ValueKind != JsonValueKind.Array)
		{
			return false;
		}

		var aggregate = isAnd;
		var settled = false;

		foreach (var operand in operands.EnumerateArray())
		{
			var childShortCircuited = shortCircuited || settled;
			var childResult = EvaluateNode(operand, variables, onLeaf, childShortCircuited);

			if (childShortCircuited)
			{
				continue;
			}

			if (isAnd)
			{
				aggregate = aggregate && childResult;
				if (!childResult)
				{
					settled = true;
				}
			}
			else
			{
				aggregate = aggregate || childResult;
				if (childResult)
				{
					settled = true;
				}
			}
		}

		return aggregate;
	}

	private static void Report(
		Action<ConditionLeafOutcome>? onLeaf,
		string id,
		bool? result,
		string leftDisplay,
		string rightDisplay,
		string? error)
	{
		if (onLeaf is null || string.IsNullOrEmpty(id))
		{
			return;
		}

		onLeaf(new ConditionLeafOutcome(id, result, leftDisplay, rightDisplay, error));
	}

	private static JsonElement ExtractSideValue(JsonElement node, string sideName)
	{
		if (!node.TryGetProperty(sideName, out var side))
		{
			return default;
		}

		if (side.ValueKind == JsonValueKind.Object && side.TryGetProperty("value", out var inner))
		{
			return inner;
		}

		return side;
	}

	public string? RenderTemplateString(string? raw, VariableContext variables)
	{
		if (string.IsNullOrEmpty(raw))
		{
			return raw;
		}

		try
		{
			return _renderer.Render(raw, variables);
		}
		catch
		{
			return raw;
		}
	}

	/// <summary>
	/// Whether <paramref name="op"/> asks about the left operand's own state and ignores the right one.
	/// Public because EventSubscriptionMatcher has to recognise a filter that carries no configured value
	/// on purpose, rather than skipping it as unconfigured.
	/// </summary>
	public static bool IsStateOperator(string? op)
		=> op is "isEmpty" or "isNotEmpty" or "isAvailable" or "isNotAvailable";

	public static bool EvaluateOperator(object? left, string op, object? right)
	{
		// Answered before any coercion, because the distinction these four exist for - "no value at all"
		// versus "the empty string" - survives only while left is still object?: Stringify below maps null
		// and "" onto the same "". Above TryGetDouble as well, or a numeric operand would be swallowed by
		// the numeric switch and fall out of its default arm.
		//
		// The two pairs are deliberately NOT negations of each other. An operand that did not resolve is
		// neither empty nor not-empty - it is unavailable, and isEmpty and isNotEmpty are both false for
		// it. Only something that resolved can be said to be empty. right is ignored entirely, so a value
		// left behind by an operator the author switched away from cannot change the verdict.
		if (IsStateOperator(op))
		{
			var available = left is not null;
			return op switch
			{
				"isAvailable" => available,
				"isNotAvailable" => !available,
				"isEmpty" => available && Stringify(left).Length == 0,
				"isNotEmpty" => available && Stringify(left).Length != 0,
				_ => false
			};
		}

		if (TryGetDouble(left, out var leftNumber) && TryGetDouble(right, out var rightNumber))
		{
			return op switch
			{
				"==" => Math.Abs(leftNumber - rightNumber) < double.Epsilon,
				"!=" => Math.Abs(leftNumber - rightNumber) >= double.Epsilon,
				">" => leftNumber > rightNumber,
				"<" => leftNumber < rightNumber,
				">=" => leftNumber >= rightNumber,
				"<=" => leftNumber <= rightNumber,
				_ => false
			};
		}

		if ((op == "==" || op == "!=") && TryGetBool(left, out var leftBool) && TryGetBool(right, out var rightBool))
		{
			var equal = leftBool == rightBool;
			return op == "==" ? equal : !equal;
		}

		var leftText = Stringify(left);
		var rightText = Stringify(right);
		return op switch
		{
			"==" => string.Equals(leftText, rightText, StringComparison.Ordinal),
			"!=" => !string.Equals(leftText, rightText, StringComparison.Ordinal),
			"contains" => leftText.Contains(rightText, StringComparison.Ordinal),
			"startsWith" => leftText.StartsWith(rightText, StringComparison.Ordinal),
			">" => string.CompareOrdinal(leftText, rightText) > 0,
			"<" => string.CompareOrdinal(leftText, rightText) < 0,
			">=" => string.CompareOrdinal(leftText, rightText) >= 0,
			"<=" => string.CompareOrdinal(leftText, rightText) <= 0,
			_ => false
		};
	}

	public static object? ResolveVariableReference(JsonElement element, VariableContext variables)
	{
		if (element.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		if (TryReadReferenceName(element, "$event", out var eventName))
		{
			return variables.TryResolveEventParameter(eventName, out var eventValue) ? eventValue : null;
		}

		if (!TryReadReferenceName(element, "$var", out var name))
		{
			return null;
		}

		// A script input shadows a global of the same name for the run, in this form as well as in
		// {{ vars.x }} - the overlay is authoritative for both.
		if (variables.TryResolveInput(name, out var input))
		{
			return input;
		}

		return variables.TryResolve(name, out var variable) ? ConvertVariableValue(variable) : null;
	}

	// Whether element carries a "$var" or "$event" key at all, independent of whether that reference goes
	// on to resolve - see ResolveSide's Object case for why this distinction matters.
	private static bool IsVariableOrEventReference(JsonElement element)
		=> TryReadReferenceName(element, "$event", out _) || TryReadReferenceName(element, "$var", out _);

	private static bool TryReadReferenceName(JsonElement element, string property, out string name)
	{
		name = string.Empty;
		if (!element.TryGetProperty(property, out var nameElement) || nameElement.ValueKind != JsonValueKind.String)
		{
			return false;
		}

		name = nameElement.GetString() ?? string.Empty;
		return name.Length > 0;
	}

	public static object? ConvertVariableValue(VariableEntity variable)
	{
		return variable.Type switch
		{
			VariableType.Text => variable.Value,
			VariableType.Numeric => double.TryParse(variable.Value,
				NumberStyles.Float,
				CultureInfo.InvariantCulture,
				out var n)
				? n
				: variable.Value,
			VariableType.Boolean => string.Equals(variable.Value, "true", StringComparison.OrdinalIgnoreCase),
			_ => variable.Value
		};
	}

	public static bool TryGetBool(object? value, out bool result)
	{
		switch (value)
		{
			case bool b:
				result = b;
				return true;
			case string s when bool.TryParse(s, out var parsed):
				result = parsed;
				return true;
			default:
				result = false;
				return false;
		}
	}

	private static string Stringify(object? value)
		=> value switch
		{
			null => string.Empty,
			bool b => b ? "true" : "false",
			_ => value.ToString() ?? string.Empty
		};

	public static bool TryGetDouble(object? value, out double number)
	{
		switch (value)
		{
			case double d:
				number = d;
				return true;
			case float f:
				number = f;
				return true;
			case int i:
				number = i;
				return true;
			case long l:
				number = l;
				return true;
			case string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
				number = parsed;
				return true;
			default:
				number = 0;
				return false;
		}
	}

	private static string FormatDisplay(object? value)
	{
		return value switch
		{
			null => string.Empty,
			double d => d.ToString(CultureInfo.InvariantCulture),
			float f => f.ToString(CultureInfo.InvariantCulture),
			decimal m => m.ToString(CultureInfo.InvariantCulture),
			bool b => b ? "true" : "false",
			_ => value.ToString() ?? string.Empty
		};
	}
}
