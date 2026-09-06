using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MacroDeck.Plugin.Analyzers;

/// <summary>
/// Finds the expression a read-only property declaration effectively returns, across the handful of
/// shapes a one-line property is normally written in - so MDP1001/MDP1002/MDP2002 do not each re-derive
/// it. Deliberately narrow: a multi-statement getter, or one that returns conditionally, has no single
/// "the" expression, so it is treated the same as a property this analyzer cannot see a constant behind -
/// skipped, not guessed at.
/// </summary>
internal static class PropertyConstantHelper
{
	/// <summary>
	/// The expression syntax <paramref name="property" /> returns, for:
	/// <c>=&gt; expr</c>, <c>{ get; } = expr</c>, <c>{ get =&gt; expr; }</c> and
	/// <c>{ get { return expr; } }</c>. <c>null</c> for anything else, including a getter with no body
	/// (an auto-property with no initializer) or more than one statement.
	/// </summary>
	public static ExpressionSyntax? GetReturnedExpression(PropertyDeclarationSyntax property)
	{
		if (property.ExpressionBody is { } propertyArrow)
		{
			return propertyArrow.Expression;
		}

		if (property.Initializer is { } initializer)
		{
			return initializer.Value;
		}

		var getter = property.AccessorList?.Accessors.FirstOrDefault(accessor
			=> accessor.IsKind(SyntaxKind.GetAccessorDeclaration));

		if (getter is null)
		{
			return null;
		}

		if (getter.ExpressionBody is { } getterArrow)
		{
			return getterArrow.Expression;
		}

		if (getter.Body is { Statements.Count: 1 } block &&
			block.Statements[0] is ReturnStatementSyntax { Expression: { } returned })
		{
			return returned;
		}

		return null;
	}

	/// <summary>The compile-time constant string <paramref name="property" /> returns, or <c>null</c>
	/// when it does not return a provable compile-time constant.</summary>
	public static string? GetConstantStringValueOrNull(
		PropertyDeclarationSyntax property,
		SemanticModel semanticModel,
		CancellationToken cancellationToken)
	{
		var expression = GetReturnedExpression(property);
		if (expression is null)
		{
			return null;
		}

		var constant = semanticModel.GetConstantValue(expression, cancellationToken);
		return constant is { HasValue: true, Value: string text } ? text : null;
	}
}
