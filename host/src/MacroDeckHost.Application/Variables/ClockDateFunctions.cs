using System.Globalization;
using Scriban;
using Scriban.Functions;
using Scriban.Parsing;
using Scriban.Runtime;
using Scriban.Syntax;

namespace MacroDeckHost.Application.Variables;

// Scriban has no Liquid "now" or "today" keyword; here both read the host clock like date.now.
internal sealed class ClockDateFunctions : DateTimeFunctions
{
	private readonly ScriptObject _overrides = new();

	public ClockDateFunctions()
	{
		_overrides.Import("parse", new Func<TemplateContext, string?, string?, string?, DateTime?>(ParseNow));
		_overrides.Import("parse_to_string",
			new Func<TemplateContext, string?, string?, string?, string?, string?, string?>(ParseNowToString));
	}

	public override bool TryGetValue(TemplateContext? context, SourceSpan span, string member, out object? value)
	{
		switch (member)
		{
			case "now":
				value = LocalNow(context);
				return true;
			case "utc_now":
				value = Clock(context).GetUtcNow().UtcDateTime;
				return true;
			default:
				return _overrides.TryGetValue(context, span, member, out value)
					|| base.TryGetValue(context, span, member, out value);
		}
	}

	public override object? Invoke(
		TemplateContext context,
		ScriptNode? callerContext,
		ScriptArray arguments,
		ScriptBlockStatement? blockStatement)
	{
		return arguments.Count == 1 && TemplateClock.IsNowKeyword(context.ObjectToString(arguments[0]))
			? LocalNow(context)
			: base.Invoke(context, callerContext, arguments, blockStatement);
	}

	private static DateTime? ParseNow(
		TemplateContext context,
		string? text,
		string? pattern = null,
		string? culture = null)
	{
		return TemplateClock.IsNowKeyword(text) ? LocalNow(context) : Parse(context, text, pattern, culture);
	}

	private string? ParseNowToString(
		TemplateContext context,
		string? text,
		string? outputPattern = null,
		string? outputCulture = null,
		string? inputPattern = null,
		string? inputCulture = null)
	{
		if (!TemplateClock.IsNowKeyword(text))
		{
			return ParseToString(context,
				text,
				outputPattern,
				outputCulture,
				inputPattern,
				inputCulture);
		}

		var culture = outputCulture is not null ? CultureInfo.GetCultureInfo(outputCulture) : context.CurrentCulture;
		return ToString(LocalNow(context), outputPattern ?? DefaultFormat, culture);
	}

	private static DateTime LocalNow(TemplateContext? context)
	{
		return Clock(context).GetLocalNow().DateTime;
	}

	private static TimeProvider Clock(TemplateContext? context)
	{
		var templateContext = (VariableTemplateContext)context!;
		templateContext.ReadsClock = true;
		return templateContext.Clock;
	}
}
