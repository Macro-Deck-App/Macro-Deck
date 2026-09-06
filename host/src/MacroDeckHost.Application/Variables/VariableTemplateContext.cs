using System.Collections;
using Scriban;
using Scriban.Parsing;

namespace MacroDeckHost.Application.Variables;

// Unwraps VariableTemplateValue everywhere Scriban asks the context to interpret a value, so an attributed
// variable behaves exactly like the bare scalar it wraps.
//
// Six overrides rather than the two the attribute mechanism would suggest, because Scriban decides several
// things before the operator hook gets a say: a binary expression whose operand is a string is routed to
// string comparison via ObjectToString, "== empty" is answered by IsEmpty, "{% for %}" goes through
// ToList, and function arguments are coerced through ToInt/ToObject. Leaving any of them out would let the
// container itself answer instead of the value.
public sealed class VariableTemplateContext : LiquidTemplateContext
{
	public override bool ToBool(SourceSpan span, object? value)
		=> base.ToBool(span, VariableTemplateValue.Unwrap(value));

	public override int ToInt(SourceSpan span, object? value)
		=> base.ToInt(span, VariableTemplateValue.Unwrap(value));

	public override object? ToObject(SourceSpan span, object? value, Type destinationType)
		=> base.ToObject(span, VariableTemplateValue.Unwrap(value), destinationType);

	public override string? ObjectToString(object? value, bool nested = false)
		=> base.ObjectToString(VariableTemplateValue.Unwrap(value), nested);

	public override object? IsEmpty(SourceSpan span, object? against)
		=> base.IsEmpty(span, VariableTemplateValue.Unwrap(against));

	public override IList? ToList(SourceSpan span, object? value)
		=> base.ToList(span, VariableTemplateValue.Unwrap(value));
}
