using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace MacroDeck.Plugin.Analyzers.Rules;

/// <summary>
/// MDP3002: <c>.Result</c>, <c>.Wait()</c>, <c>.GetAwaiter().GetResult()</c> or <c>Thread.Sleep</c>
/// inside a member of a type implementing <c>ICapabilityHandler</c>, <c>IActionExecutor</c> or
/// <c>IConfigFlow</c>.
///
/// <para>
/// Scoped to the whole type, not only its interface methods: a private helper the async entry point
/// calls blocks the dispatcher exactly as much as blocking directly in that entry point would, and
/// tracing whether a given helper is actually reachable from the interface method would need a call-graph
/// analysis this package does not attempt.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BlockingCallAnalyzer : DiagnosticAnalyzer
{
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
		[DiagnosticDescriptors.BlockingCall];

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		context.RegisterCompilationStartAction(startContext =>
		{
			var compilation = startContext.Compilation;
			var handlerType = compilation.GetTypeByMetadataName(WellKnownTypeNames.CapabilityHandler);
			var executorType = compilation.GetTypeByMetadataName(WellKnownTypeNames.ActionExecutor);
			var configFlowType = compilation.GetTypeByMetadataName(WellKnownTypeNames.ConfigFlow);
			var taskType = compilation.GetTypeByMetadataName(WellKnownTypeNames.Task);
			var taskOfTType = compilation.GetTypeByMetadataName(WellKnownTypeNames.TaskOfT);
			var valueTaskOfTType = compilation.GetTypeByMetadataName(WellKnownTypeNames.ValueTaskOfT);
			var threadType = compilation.GetTypeByMetadataName(WellKnownTypeNames.Thread);

			if (handlerType is null && executorType is null && configFlowType is null)
			{
				return;
			}

			startContext.RegisterOperationBlockStartAction(blockStartContext =>
			{
				var contractName = DescribeImplementedContract(blockStartContext.OwningSymbol.ContainingType,
					handlerType,
					executorType,
					configFlowType);

				if (contractName is null)
				{
					return;
				}

				blockStartContext.RegisterOperationAction(operationContext =>
					{
						var propertyReference = (IPropertyReferenceOperation)operationContext.Operation;
						var property = propertyReference.Property;
						var containingType = property.ContainingType?.OriginalDefinition;

						if (property.Name != "Result" ||
							(!SymbolEqualityComparer.Default.Equals(containingType, taskOfTType) &&
								!SymbolEqualityComparer.Default.Equals(containingType, valueTaskOfTType)))
						{
							return;
						}

						Report(operationContext, ".Result", contractName, propertyReference.Syntax.GetLocation());
					},
					OperationKind.PropertyReference);

				blockStartContext.RegisterOperationAction(operationContext =>
					{
						var invocation = (IInvocationOperation)operationContext.Operation;
						var method = invocation.TargetMethod;

						if (method.Name == "Wait" &&
							taskType is not null &&
							invocation.Instance?.Type.IsOrDerivesFrom(taskType) == true)
						{
							Report(operationContext, ".Wait()", contractName, invocation.Syntax.GetLocation());
							return;
						}

						if (method.Name == "GetResult" &&
							invocation.Instance is IInvocationOperation { TargetMethod.Name: "GetAwaiter" })
						{
							Report(operationContext,
								".GetAwaiter().GetResult()",
								contractName,
								invocation.Syntax.GetLocation());
							return;
						}

						if (method.Name == "Sleep" &&
							SymbolEqualityComparer.Default.Equals(method.ContainingType, threadType))
						{
							Report(operationContext, "Thread.Sleep", contractName, invocation.Syntax.GetLocation());
						}
					},
					OperationKind.Invocation);
			});
		});
	}

	private static void Report(OperationAnalysisContext context, string pattern, string contractName, Location location)
		=> context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.BlockingCall,
			location,
			pattern,
			contractName));

	private static string? DescribeImplementedContract(
		ITypeSymbol? type,
		INamedTypeSymbol? handlerType,
		INamedTypeSymbol? executorType,
		INamedTypeSymbol? configFlowType)
	{
		if (type is null)
		{
			return null;
		}

		if (type.IsOrImplements(handlerType))
		{
			return "ICapabilityHandler";
		}

		if (type.IsOrImplements(executorType))
		{
			return "IActionExecutor";
		}

		if (type.IsOrImplements(configFlowType))
		{
			return "IConfigFlow";
		}

		return null;
	}
}
