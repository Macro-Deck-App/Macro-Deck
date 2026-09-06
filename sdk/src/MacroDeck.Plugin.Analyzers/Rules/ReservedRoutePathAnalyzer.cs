using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace MacroDeck.Plugin.Analyzers.Rules;

/// <summary>MDP2005: <c>MapGet</c>/<c>MapPost</c>/... with a constant path matching
/// <c>ReservedPaths.IsReserved</c>.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ReservedRoutePathAnalyzer : DiagnosticAnalyzer
{
	private static readonly ImmutableHashSet<string> _mapMethodNames = ImmutableHashSet.Create("Map",
		"MapGet",
		"MapPost",
		"MapPut",
		"MapDelete",
		"MapPatch",
		"MapMethods");

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
		[DiagnosticDescriptors.ReservedRoutePath];

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		context.RegisterCompilationStartAction(startContext =>
		{
			var routeBuilderType
				= startContext.Compilation.GetTypeByMetadataName(WellKnownTypeNames.EndpointRouteBuilder);
			if (routeBuilderType is null)
			{
				return;
			}

			startContext.RegisterOperationAction(operationContext =>
				{
					var invocation = (IInvocationOperation)operationContext.Operation;
					var method = invocation.TargetMethod;

					var receiverType = invocation.GetReceiverType();

					if (!_mapMethodNames.Contains(method.Name) || !receiverType.IsOrImplements(routeBuilderType))
					{
						return;
					}

					// Found by parameter name, not position: depending on which of the two IOperation shapes
					// GetReceiverType's remarks describe this invocation got, argument position 0 is either
					// the pattern or the receiver.
					var patternArgument = invocation.GetArgument("pattern");
					if (patternArgument is null)
					{
						return;
					}

					var pattern = patternArgument.Value.GetConstantStringValueOrNull();
					if (pattern is null || !ReservedPathHeuristic.IsReserved(pattern))
					{
						return;
					}

					operationContext.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.ReservedRoutePath,
						patternArgument.Value.Syntax.GetLocation(),
						pattern));
				},
				OperationKind.Invocation);
		});
	}
}
