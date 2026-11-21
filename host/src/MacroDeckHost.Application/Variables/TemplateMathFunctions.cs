using System.Globalization;
using Scriban;
using Scriban.Functions;
using Scriban.Parsing;

namespace MacroDeckHost.Application.Variables;

// Text that holds a number is that number, any other text comes back unchanged.
internal static class TemplateMathFunctions
{
	public static object? Abs(TemplateContext context, SourceSpan span, object? value)
	{
		return TryNumber(value, out var number) ? MathFunctions.Abs(context, span, number) : value;
	}

	public static object? Ceil(TemplateContext context, SourceSpan span, object? value)
	{
		return TryNumber(value, out var number)
			? MathFunctions.Ceil(context.ToObject<double>(span, number))
			: value;
	}

	public static object? Floor(TemplateContext context, SourceSpan span, object? value)
	{
		return TryNumber(value, out var number)
			? MathFunctions.Floor(context.ToObject<double>(span, number))
			: value;
	}

	public static object? Round(TemplateContext context, SourceSpan span, object? value, int precision = 0)
	{
		return TryNumber(value, out var number)
			? MathFunctions.Round(context.ToObject<double>(span, number), precision)
			: value;
	}

	public static object? DividedBy(TemplateContext context, SourceSpan span, object? value, object? divisor)
	{
		return TryNumber(value, out var number)
			? MathFunctions.DividedBy(context, span, context.ToObject<double>(span, number), Operand(divisor))
			: value;
	}

	public static object? Plus(TemplateContext context, SourceSpan span, object? value, object? with)
	{
		return TryNumber(value, out var number) ? MathFunctions.Plus(context, span, number, Operand(with)) : value;
	}

	public static object? Minus(TemplateContext context, SourceSpan span, object? value, object? with)
	{
		return TryNumber(value, out var number) ? MathFunctions.Minus(context, span, number, Operand(with)) : value;
	}

	public static object? Times(TemplateContext context, SourceSpan span, object? value, object? with)
	{
		return TryNumber(value, out var number) ? MathFunctions.Times(context, span, number, Operand(with)) : value;
	}

	public static object? Modulo(TemplateContext context, SourceSpan span, object? value, object? with)
	{
		return TryNumber(value, out var number) ? MathFunctions.Modulo(context, span, number, Operand(with)) : value;
	}

	private static bool TryNumber(object? value, out object? number)
	{
		number = VariableTemplateValue.Unwrap(value);
		if (number is not string text)
		{
			return true;
		}

		if (!TryParse(text, out var parsed))
		{
			return false;
		}

		number = parsed;
		return true;
	}

	private static object? Operand(object? value)
	{
		var unwrapped = VariableTemplateValue.Unwrap(value);
		return unwrapped is string text && TryParse(text, out var parsed) ? parsed : unwrapped;
	}

	private static bool TryParse(string text, out decimal number)
	{
		return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
	}
}
