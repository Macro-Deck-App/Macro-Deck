using Scriban;
using Scriban.Functions;
using Scriban.Syntax;

namespace MacroDeckHost.Application.Variables;

internal static class TemplateVariableAccess
{
	private const string VarsName = "vars";

	private static readonly string[] _pureFilters =
	[
		"append", "capitalize", "downcase", "lstrip", "prepend", "remove", "remove_first", "replace",
		"replace_first", "rstrip", "strip", "strip_newlines", "truncate", "truncatewords", "upcase", "abs", "ceil",
		"divided_by", "floor", "minus", "modulo", "plus", "round", "times", "escape", "escape_once",
		"newline_to_br", "strip_html", "default", "size",
	];

	private static readonly HashSet<string> _bareCallees =
		new(_pureFilters.Append(TemplateFilters.PlainTextName), StringComparer.Ordinal);

	private static readonly HashSet<(string Target, string Member)> _memberCallees = BuildMemberCallees();

	public static IReadOnlyCollection<string>? ReadNames(Template template)
	{
		if (template.HasErrors || template.Page is null)
		{
			return null;
		}

		var names = new HashSet<string>(StringComparer.Ordinal);
		return Visit(template.Page, names) ? names : null;
	}

	// Only listed shapes may use a reduced context: anything else, however harmless it looks, could read a
	// variable the walk cannot name (object.eval_template renders a runtime string against the whole context).
	private static bool Visit(ScriptNode? node, HashSet<string> names)
	{
		switch (node)
		{
			case null:
				return true;
			case ScriptMemberExpression or ScriptIndexerExpression when TryReadVariable(node, out var name):
				names.Add(name);
				return true;
			case ScriptFunctionCall call:
				return IsPureCallee(call.Target) && call.Arguments.All(argument => Visit(argument, names));
			case ScriptPage
				or ScriptBlockStatement
				or ScriptRawStatement
				or ScriptEscapeStatement
				or ScriptExpressionStatement
				or ScriptIfStatement
				or ScriptElseStatement
				or ScriptEndStatement
				or ScriptNopStatement
				or ScriptKeyword
				or ScriptToken
				or ScriptList
				or ScriptPipeCall
				or ScriptNestedExpression
				or ScriptLiteral
				or ScriptBinaryExpression
				or ScriptUnaryExpression:
				return node.Children.All(child => Visit(child, names));
			default:
				return false;
		}
	}

	private static bool TryReadVariable(ScriptNode node, out string name)
	{
		var current = node;

		while (current is ScriptMemberExpression { Target: var target, Member: ScriptVariable } member &&
			!IsVars(target))
		{
			current = member.Target;
		}

		switch (current)
		{
			case ScriptMemberExpression { Target: var root, Member: ScriptVariable variable } when IsVars(root):
				name = variable.Name;
				return true;
			case ScriptIndexerExpression { Target: var root, Index: ScriptLiteral { Value: string literal } }
				when IsVars(root):
				name = literal;
				return true;
			default:
				name = string.Empty;
				return false;
		}
	}

	private static bool IsVars(ScriptExpression? expression)
		=> expression is ScriptVariableGlobal { Name: VarsName };

	private static bool IsPureCallee(ScriptExpression? callee)
		=> callee switch
		{
			ScriptVariableGlobal global => _bareCallees.Contains(global.Name),
			ScriptMemberExpression { Target: ScriptVariableGlobal target, Member: ScriptVariable member }
				=> _memberCallees.Contains((target.Name, member.Name)),
			_ => false,
		};

	private static HashSet<(string Target, string Member)> BuildMemberCallees()
	{
		var callees = new HashSet<(string Target, string Member)>();

		foreach (var filter in _pureFilters)
		{
			if (LiquidBuiltinsFunctions.TryLiquidToScriban(filter, out var target, out var member) &&
				target is not null &&
				member is not null)
			{
				callees.Add((target, member));
			}
		}

		return callees;
	}
}
