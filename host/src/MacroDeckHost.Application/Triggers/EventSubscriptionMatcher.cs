using System.Text.Json;
using MacroDeckHost.Application.Variables;

namespace MacroDeckHost.Application.Triggers;

public interface IEventSubscriptionMatcher
{
	bool Matches(EventSubscription subscription, EventDefinitionDescriptor descriptor, VariableContext context);
}

public sealed class EventSubscriptionMatcher : IEventSubscriptionMatcher
{
	private readonly IActionConditionEvaluator _evaluator;

	public EventSubscriptionMatcher(IActionConditionEvaluator evaluator)
	{
		_evaluator = evaluator;
	}

	public bool Matches(EventSubscription subscription, EventDefinitionDescriptor descriptor, VariableContext context)
	{
		foreach (var required in descriptor.Definition.ConfigurationParameters.Where(parameter => parameter.Required))
		{
			if (!descriptor.Definition.PayloadParameters.Any(payload => payload.Name == required.Name))
			{
				continue;
			}

			if (!subscription.Configuration.TryGetValue(required.Name, out var configuredRequired))
			{
				return false;
			}

			// A state filter is configured by its operator alone, so "required" is already satisfied - the
			// unset check below would otherwise reject the subscription for the very shape these operators
			// have.
			if (ActionConditionEvaluator.IsStateOperator(configuredRequired.Operator))
			{
				continue;
			}

			if (IsUnset(_evaluator.ResolveSide(configuredRequired.Value, context).Value))
			{
				return false;
			}
		}

		foreach (var payload in descriptor.Definition.PayloadParameters)
		{
			if (!subscription.Configuration.TryGetValue(payload.Name, out var configured))
			{
				continue;
			}

			// Ahead of the unset skip and the missing-parameter rejection below, both of which are wrong
			// for a state filter: it carries no configured value on purpose, and a parameter the
			// occurrence never delivered is exactly what isNotAvailable is asking about rather than an
			// automatic non-match.
			if (ActionConditionEvaluator.IsStateOperator(configured.Operator))
			{
				context.TryResolveEventParameter(payload.Name, out var delivered);
				if (!Holds(delivered, configured.Operator, null))
				{
					return false;
				}

				continue;
			}

			var expected = _evaluator.ResolveSide(configured.Value, context);
			if (IsUnset(expected.Value))
			{
				continue;
			}

			if (!context.TryResolveEventParameter(payload.Name, out var actual) ||
				!Holds(actual, configured.Operator, expected.Value))
			{
				return false;
			}
		}

		if (subscription.Filter is not { } filter || filter.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
		{
			return true;
		}

		return _evaluator.EvaluateExpression(filter, context);
	}

	private static bool IsUnset(object? value) => value switch
	{
		null => true,
		string text => text.Length == 0,
		_ => false
	};

	private static bool Holds(object? actual, string @operator, object? expected)
		=> ActionConditionEvaluator.EvaluateOperator(actual, @operator, expected);

	private static bool TryReadLiteral(JsonElement element, out object? value)
	{
		switch (element.ValueKind)
		{
			case JsonValueKind.String:
				value = element.GetString();
				return true;

			case JsonValueKind.Number:
				value = element.TryGetInt64(out var i) ? i : element.GetDouble();
				return true;

			default:
				value = null;
				return false;
		}
	}

	public static bool QuickReject(EventSubscription subscription, EventOccurrence occurrence)
	{
		if (occurrence.Target is not null || subscription.Configuration.Count == 0)
		{
			return false;
		}

		foreach (var (name, configured) in subscription.Configuration)
		{
			// Quick-reject is a literal-only fast path over the raw occurrence. A state filter's verdict
			// turns on whether the parameter was delivered at all, which is the full Matches pass's job -
			// and a literal left behind by an operator the author switched away from must not be read here.
			if (ActionConditionEvaluator.IsStateOperator(configured.Operator))
			{
				continue;
			}

			if (!TryReadLiteral(configured.Value, out var expected) ||
				!occurrence.Parameters.TryGetValue(name, out var actual))
			{
				continue;
			}

			if (expected is string text)
			{
				if (text.Length == 0)
				{
					continue;
				}

				if (VariableTemplateRenderer.ContainsLiquid(text))
				{
					continue;
				}
			}

			if (!Holds(actual, configured.Operator, expected))
			{
				return true;
			}
		}

		return false;
	}
}
