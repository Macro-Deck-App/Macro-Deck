using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace MacroDeck.Plugin.Analyzers;

/// <summary>Small, shared symbol and operation queries every analyzer in this package builds on.</summary>
internal static class AnalysisExtensions
{
	/// <summary>Whether <paramref name="type" /> implements <paramref name="interfaceSymbol" />, directly
	/// or transitively. A type does not "implement" itself by this check - see
	/// <see cref="IsOrImplements" /> for that.</summary>
	public static bool ImplementsInterface(this ITypeSymbol? type, INamedTypeSymbol? interfaceSymbol)
	{
		if (type is null || interfaceSymbol is null)
		{
			return false;
		}

		foreach (var candidate in type.AllInterfaces)
		{
			if (SymbolEqualityComparer.Default.Equals(candidate, interfaceSymbol))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>Whether <paramref name="type" /> is <paramref name="other" /> itself or implements it.</summary>
	public static bool IsOrImplements(this ITypeSymbol? type, INamedTypeSymbol? other)
		=> other is not null && (SymbolEqualityComparer.Default.Equals(type, other) || type.ImplementsInterface(other));

	/// <summary>Whether any type in <paramref name="type" />'s base-class chain (itself included) is
	/// <paramref name="baseType" />.</summary>
	public static bool IsOrDerivesFrom(this ITypeSymbol? type, INamedTypeSymbol? baseType)
	{
		if (baseType is null)
		{
			return false;
		}

		for (var current = type; current is not null; current = current.BaseType)
		{
			if (SymbolEqualityComparer.Default.Equals(current, baseType))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// The method <paramref name="type" /> runs for <paramref name="interfaceMethod" />, or
	/// <c>null</c> when <paramref name="type" /> does not implement that interface at all.
	/// </summary>
	public static IMethodSymbol? FindImplementation(this ITypeSymbol type, IMethodSymbol? interfaceMethod)
		=> interfaceMethod is null ? null : type.FindImplementationForInterfaceMember(interfaceMethod) as IMethodSymbol;

	/// <summary>
	/// The property <paramref name="type" /> runs for <paramref name="interfaceProperty" />, or
	/// <c>null</c> when <paramref name="type" /> does not implement that interface at all.
	/// </summary>
	public static IPropertySymbol? FindImplementation(this ITypeSymbol type, IPropertySymbol? interfaceProperty)
		=> interfaceProperty is null
			? null
			: type.FindImplementationForInterfaceMember(interfaceProperty) as IPropertySymbol;

	/// <summary>
	/// The receiver of <paramref name="invocation" /> - the <c>endpoints</c> in
	/// <c>endpoints.MapGet(...)</c> - regardless of which of the two shapes IOperation happens to give an
	/// extension method call: sometimes <see cref="IInvocationOperation.Instance" /> holds it (a properly
	/// "reduced" call), and sometimes <see cref="IInvocationOperation.Instance" /> is <c>null</c> and the
	/// receiver is <see cref="IInvocationOperation.Arguments" />'s entry for the extension method's own
	/// first (the <c>this</c>) parameter instead - observed directly against real ASP.NET Core extension
	/// methods, not documented behaviour a caller could otherwise rely on.
	/// </summary>
	public static ITypeSymbol? GetReceiverType(this IInvocationOperation invocation)
	{
		if (invocation.Instance is not null)
		{
			return invocation.Instance.Type;
		}

		if (!invocation.TargetMethod.IsExtensionMethod || invocation.TargetMethod.Parameters.Length == 0)
		{
			return null;
		}

		var receiverParameter = invocation.TargetMethod.Parameters[0];

		foreach (var argument in invocation.Arguments)
		{
			if (SymbolEqualityComparer.Default.Equals(argument.Parameter, receiverParameter))
			{
				return argument.Value.Type;
			}
		}

		return null;
	}

	/// <summary>
	/// The argument bound to the parameter named <paramref name="parameterName" />, or <c>null</c> when
	/// there is none. Safe to use regardless of argument order in the source (named arguments, an
	/// extension method's receiver landing in <see cref="IInvocationOperation.Arguments" /> or not - see
	/// <see cref="GetReceiverType" />) because it matches by parameter identity, never by position.
	/// </summary>
	public static IArgumentOperation? GetArgument(this IInvocationOperation invocation, string parameterName)
	{
		foreach (var argument in invocation.Arguments)
		{
			if (argument.Parameter?.Name == parameterName)
			{
				return argument;
			}
		}

		return null;
	}

	/// <summary>
	/// The compile-time constant string value of this operation, or <c>null</c> when it is not a
	/// compile-time constant - a variable, a method call, or string interpolation of a non-constant value
	/// all return <c>null</c> here. A literal, a <c>const</c> reference, <c>nameof(...)</c>, and
	/// concatenation of other constants are all constants by this check, exactly as the C# language
	/// defines a constant expression.
	/// </summary>
	public static string? GetConstantStringValueOrNull(this IOperation? operation)
	{
		if (operation is null)
		{
			return null;
		}

		var constant = operation.ConstantValue;
		return constant is { HasValue: true, Value: string text } ? text : null;
	}
}
