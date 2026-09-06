using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace MacroDeck.Plugin.Analyzers.Rules;

/// <summary>
/// MDP3001: inside <c>ICapabilityHandler.InvokeAsync</c> or <c>IActionExecutor.ExecuteAsync</c>, a call
/// that passes <c>default</c>/<c>CancellationToken.None</c> for a <c>CancellationToken</c> parameter, or
/// omits an optional one, while a real token is reachable right there - the method's own
/// <c>cancellationToken</c> parameter for <c>InvokeAsync</c>, or <c>context.CancellationToken</c> for
/// <c>ExecuteAsync</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingCancellationForwardingAnalyzer : DiagnosticAnalyzer
{
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
		[DiagnosticDescriptors.MissingCancellationForwarding];

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		context.RegisterCompilationStartAction(startContext =>
		{
			var compilation = startContext.Compilation;
			var handlerType = compilation.GetTypeByMetadataName(WellKnownTypeNames.CapabilityHandler);
			var executorType = compilation.GetTypeByMetadataName(WellKnownTypeNames.ActionExecutor);
			var cancellationTokenType = compilation.GetTypeByMetadataName(WellKnownTypeNames.CancellationToken);
			var actionExecutionContextType
				= compilation.GetTypeByMetadataName(WellKnownTypeNames.ActionExecutionContext);

			if (cancellationTokenType is null || (handlerType is null && executorType is null))
			{
				return;
			}

			var invokeAsyncInterfaceMethod
				= handlerType?.GetMembers("InvokeAsync").OfType<IMethodSymbol>().FirstOrDefault();
			var executeAsyncInterfaceMethod
				= executorType?.GetMembers("ExecuteAsync").OfType<IMethodSymbol>().FirstOrDefault();

			startContext.RegisterOperationBlockStartAction(blockStartContext =>
			{
				if (blockStartContext.OwningSymbol is not IMethodSymbol method)
				{
					return;
				}

				var tokenDisplayName = DescribeInScopeToken(method,
					invokeAsyncInterfaceMethod,
					executeAsyncInterfaceMethod,
					cancellationTokenType,
					actionExecutionContextType);

				if (tokenDisplayName is null)
				{
					return;
				}

				blockStartContext.RegisterOperationAction(operationContext =>
					{
						var invocation = (IInvocationOperation)operationContext.Operation;

						foreach (var argument in invocation.Arguments)
						{
							if (argument.Parameter is not { } parameter ||
								!SymbolEqualityComparer.Default.Equals(parameter.Type, cancellationTokenType) ||
								!IsNoCancellation(argument, cancellationTokenType))
							{
								continue;
							}

							var location = argument.IsImplicit
								? invocation.Syntax.GetLocation()
								: argument.Syntax.GetLocation();

							operationContext.ReportDiagnostic(Diagnostic.Create(
								DiagnosticDescriptors.MissingCancellationForwarding,
								location,
								tokenDisplayName));
						}
					},
					OperationKind.Invocation);
			});
		});
	}

	private static bool IsNoCancellation(IArgumentOperation argument, INamedTypeSymbol cancellationTokenType)
	{
		// The parameter has a default value and the caller supplied nothing at all for it.
		if (argument.ArgumentKind == ArgumentKind.DefaultValue)
		{
			return true;
		}

		var value = argument.Value;
		while (value is IConversionOperation conversion)
		{
			value = conversion.Operand;
		}

		return value switch
		{
			IDefaultValueOperation => true,
			IFieldReferenceOperation { Field.Name: "None" } field
				=> SymbolEqualityComparer.Default.Equals(field.Field.ContainingType, cancellationTokenType),
			IPropertyReferenceOperation { Property.Name: "None" } property
				=> SymbolEqualityComparer.Default.Equals(property.Property.ContainingType, cancellationTokenType),
			_ => false
		};
	}

	/// <summary>
	/// The expression that names the real, live cancellation token reachable in <paramref name="method" />
	/// - "cancellationToken" for <c>InvokeAsync</c>, "context.CancellationToken" for <c>ExecuteAsync</c> -
	/// or <c>null</c> when <paramref name="method" /> is neither.
	/// </summary>
	private static string? DescribeInScopeToken(
		IMethodSymbol method,
		IMethodSymbol? invokeAsyncInterfaceMethod,
		IMethodSymbol? executeAsyncInterfaceMethod,
		INamedTypeSymbol cancellationTokenType,
		INamedTypeSymbol? actionExecutionContextType)
	{
		if (invokeAsyncInterfaceMethod is not null &&
			SymbolEqualityComparer.Default.Equals(method.ContainingType.FindImplementation(invokeAsyncInterfaceMethod),
				method))
		{
			var tokenParameter = method.Parameters.FirstOrDefault(parameter
				=> SymbolEqualityComparer.Default.Equals(parameter.Type, cancellationTokenType));

			return tokenParameter?.Name;
		}

		if (executeAsyncInterfaceMethod is not null &&
			actionExecutionContextType is not null &&
			SymbolEqualityComparer.Default.Equals(method.ContainingType.FindImplementation(executeAsyncInterfaceMethod),
				method))
		{
			var contextParameter = method.Parameters.FirstOrDefault(parameter
				=> SymbolEqualityComparer.Default.Equals(parameter.Type, actionExecutionContextType));

			return contextParameter is null ? null : $"{contextParameter.Name}.CancellationToken";
		}

		return null;
	}
}
