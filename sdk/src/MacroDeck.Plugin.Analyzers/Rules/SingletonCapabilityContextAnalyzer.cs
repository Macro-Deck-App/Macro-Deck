using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace MacroDeck.Plugin.Analyzers.Rules;

/// <summary>
/// MDP4001: a type registered as a process singleton whose constructor takes
/// <c>ICapabilityInvocationContext</c> - a scoped service that only exists inside one invocation, which
/// <c>PluginHostBuilder</c>'s <c>ValidateScopes</c> setting turns into a startup crash the first time the
/// container tries to construct it.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SingletonCapabilityContextAnalyzer : DiagnosticAnalyzer
{
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
		[DiagnosticDescriptors.SingletonCapabilityContext];

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		context.RegisterCompilationStartAction(startContext =>
		{
			var compilation = startContext.Compilation;
			var contextType = compilation.GetTypeByMetadataName(WellKnownTypeNames.CapabilityInvocationContext);
			var addExtensionsType =
				compilation.GetTypeByMetadataName(WellKnownTypeNames.ServiceCollectionServiceExtensions);
			var tryAddExtensionsType =
				compilation.GetTypeByMetadataName(WellKnownTypeNames.ServiceCollectionDescriptorExtensions);
			var macroDeckExtensionsType =
				compilation.GetTypeByMetadataName(WellKnownTypeNames.MacroDeckServiceCollectionExtensions);
			var pluginHostBuilderType =
				compilation.GetTypeByMetadataName(WellKnownTypeNames.PluginHostBuilder);

			if (contextType is null)
			{
				return;
			}

			startContext.RegisterOperationAction(operationContext =>
				{
					var invocation = (IInvocationOperation)operationContext.Operation;

					var implementation = ResolveSingletonImplementationType(invocation.TargetMethod,
						addExtensionsType,
						tryAddExtensionsType,
						macroDeckExtensionsType,
						pluginHostBuilderType);

					if (implementation is null ||
						!AllPublicConstructorsTakeContext(implementation, contextType, out var offendingParameter))
					{
						return;
					}

					var location = offendingParameter?.Locations.FirstOrDefault() ?? invocation.Syntax.GetLocation();

					operationContext.ReportDiagnostic(Diagnostic.Create(
						DiagnosticDescriptors.SingletonCapabilityContext,
						location,
						implementation.Name));
				},
				OperationKind.Invocation);
		});
	}

	/// <summary>
	/// The concrete type a singleton-registration call constructs, for the shapes that register a plugin
	/// or integration author's own type as a process singleton: <c>AddSingleton&lt;T&gt;()</c>,
	/// <c>AddSingleton&lt;TService, T&gt;()</c>, their <c>TryAddSingleton</c> equivalents, the SDK's own
	/// internal <c>AddMacroDeckIntegration&lt;T&gt;</c> / <c>AddMacroDeckCapabilityHandler&lt;T&gt;</c>
	/// sugar (both of which register singleton internally, still reachable from
	/// <c>MacroDeck.Plugin.Hosting.Tests.UnitTests</c> and the other assemblies its
	/// <c>InternalsVisibleTo</c> names), and the author-facing
	/// <c>PluginHostBuilder.RegisterIntegration&lt;T&gt;()</c> / <c>RegisterCapabilityHandler&lt;T&gt;()</c>
	/// door onto the same sugar - the shape a plugin project actually calls, since it cannot see the
	/// internal one.
	/// </summary>
	private static INamedTypeSymbol? ResolveSingletonImplementationType(
		IMethodSymbol method,
		INamedTypeSymbol? addExtensionsType,
		INamedTypeSymbol? tryAddExtensionsType,
		INamedTypeSymbol? macroDeckExtensionsType,
		INamedTypeSymbol? pluginHostBuilderType)
	{
		var isAddSingleton = method.Name == "AddSingleton" &&
			SymbolEqualityComparer.Default.Equals(method.ContainingType, addExtensionsType);
		var isTryAddSingleton = method.Name == "TryAddSingleton" &&
			SymbolEqualityComparer.Default.Equals(method.ContainingType, tryAddExtensionsType);

		if (isAddSingleton || isTryAddSingleton)
		{
			return method.TypeArguments.Length switch
			{
				1 => method.TypeArguments[0] as INamedTypeSymbol,
				2 => method.TypeArguments[1] as INamedTypeSymbol,
				_ => null
			};
		}

		if (macroDeckExtensionsType is not null &&
			SymbolEqualityComparer.Default.Equals(method.ContainingType, macroDeckExtensionsType) &&
			method.Name is "AddMacroDeckIntegration" or "AddMacroDeckCapabilityHandler" &&
			method.TypeArguments.Length >= 1)
		{
			return method.TypeArguments[0] as INamedTypeSymbol;
		}

		if (pluginHostBuilderType is not null &&
			SymbolEqualityComparer.Default.Equals(method.ContainingType, pluginHostBuilderType) &&
			method.Name is "RegisterIntegration" or "RegisterCapabilityHandler" &&
			method.TypeArguments.Length >= 1)
		{
			return method.TypeArguments[0] as INamedTypeSymbol;
		}

		return null;
	}

	/// <summary>
	/// Whether every public instance constructor of <paramref name="type" /> takes
	/// <paramref name="contextType" />. Deliberately not "the constructor dependency injection would
	/// actually pick" - replicating that selection algorithm risks a false positive on an Error-severity
	/// rule, so a type with a constructor DI would never reach that avoids the parameter is left alone.
	/// </summary>
	private static bool AllPublicConstructorsTakeContext(
		INamedTypeSymbol type,
		INamedTypeSymbol contextType,
		out IParameterSymbol? offendingParameter)
	{
		offendingParameter = null;

		var publicConstructors = type.InstanceConstructors
			.Where(constructor => constructor.DeclaredAccessibility == Accessibility.Public)
			.ToImmutableArray();

		if (publicConstructors.IsEmpty)
		{
			return false;
		}

		foreach (var constructor in publicConstructors)
		{
			var parameter = constructor.Parameters
				.FirstOrDefault(candidate => SymbolEqualityComparer.Default.Equals(candidate.Type, contextType));

			if (parameter is null)
			{
				return false;
			}

			offendingParameter ??= parameter;
		}

		return true;
	}
}
