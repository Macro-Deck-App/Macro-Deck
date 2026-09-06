using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace MacroDeck.Plugin.Analyzers.Rules;

/// <summary>
/// MDP2003: <c>services.AddSingleton&lt;T&gt;()</c> where <c>T : ICapabilityHandler</c> and no
/// <c>ICapabilityHandler</c> service type is registered for it anywhere in the same registration method
/// or lambda, so the handler is constructed but never added to the collection the capability catalog is
/// built from.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnroutedCapabilityHandlerAnalyzer : DiagnosticAnalyzer
{
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
		[DiagnosticDescriptors.UnroutedCapabilityHandler];

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		context.RegisterCompilationStartAction(startContext =>
		{
			var compilation = startContext.Compilation;
			var handlerType = compilation.GetTypeByMetadataName(WellKnownTypeNames.CapabilityHandler);
			var addExtensionsType =
				compilation.GetTypeByMetadataName(WellKnownTypeNames.ServiceCollectionServiceExtensions);
			var tryAddExtensionsType =
				compilation.GetTypeByMetadataName(WellKnownTypeNames.ServiceCollectionDescriptorExtensions);
			var macroDeckExtensionsType =
				compilation.GetTypeByMetadataName(WellKnownTypeNames.MacroDeckServiceCollectionExtensions);

			if (handlerType is null || (addExtensionsType is null && tryAddExtensionsType is null))
			{
				return;
			}

			startContext.RegisterOperationBlockStartAction(blockStartContext =>
			{
				var selfRegistrations = new List<(ITypeSymbol Implementation, IInvocationOperation Invocation)>();
				var routedImplementations = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);

				blockStartContext.RegisterOperationAction(operationContext =>
					{
						var invocation = (IInvocationOperation)operationContext.Operation;
						var method = invocation.TargetMethod;

						var isSingletonFamily =
							(method.Name == "AddSingleton" &&
								SymbolEqualityComparer.Default.Equals(method.ContainingType, addExtensionsType)) ||
							(method.Name == "TryAddSingleton" &&
								SymbolEqualityComparer.Default.Equals(method.ContainingType, tryAddExtensionsType));

						if (isSingletonFamily)
						{
							if (method.TypeArguments.Length == 1 &&
								method.TypeArguments[0].ImplementsInterface(handlerType))
							{
								selfRegistrations.Add((method.TypeArguments[0], invocation));
							}
							else if (method.TypeArguments.Length == 2 &&
								SymbolEqualityComparer.Default.Equals(method.TypeArguments[0], handlerType))
							{
								routedImplementations.Add(method.TypeArguments[1]);
							}

							return;
						}

						if (macroDeckExtensionsType is not null &&
							method.Name == "AddMacroDeckCapabilityHandler" &&
							method.TypeArguments.Length == 1 &&
							SymbolEqualityComparer.Default.Equals(method.ContainingType, macroDeckExtensionsType))
						{
							routedImplementations.Add(method.TypeArguments[0]);
						}
					},
					OperationKind.Invocation);

				blockStartContext.RegisterOperationBlockEndAction(endContext =>
				{
					foreach (var (implementation, invocation) in selfRegistrations)
					{
						if (routedImplementations.Contains(implementation))
						{
							continue;
						}

						endContext.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.UnroutedCapabilityHandler,
							invocation.Syntax.GetLocation(),
							implementation.Name));
					}
				});
			});
		});
	}
}
