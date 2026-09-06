using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace MacroDeck.Plugin.Analyzers.Rules;

/// <summary>
/// MDP2004: <c>services.AddSingleton&lt;T&gt;()</c> or <c>AddSingleton&lt;IPluginIntegration, T&gt;()</c>
/// where <c>T : IPluginIntegration</c>, instead of <c>builder.RegisterIntegration&lt;T&gt;()</c>.
///
/// <para>
/// Fires on <c>IServiceCollection.AddSingleton</c>, because that is where a plugin author's registration
/// call actually happens - a service-configuration callback given an <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection" />.
/// The fix does not live there, though: <c>AddMacroDeckIntegration&lt;T&gt;</c> is internal to
/// <c>MacroDeck.Plugin.Hosting</c> now (a plugin project cannot see or call it), so the message points at
/// <c>PluginHostBuilder.RegisterIntegration&lt;T&gt;()</c>, the one public door onto it.
/// </para>
///
/// <para>
/// Scoped to exactly the two shapes the SDK itself never produces:
/// <c>RegisterIntegration&lt;TIntegration&gt;</c> internally calls
/// <c>services.AddSingleton&lt;IPluginIntegration&gt;(factory)</c> - one type argument that <em>is</em>
/// <c>IPluginIntegration</c>, not a type that implements it - which this rule has to leave alone or it
/// would fire on the SDK's own correct registration path.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RawIntegrationRegistrationAnalyzer : DiagnosticAnalyzer
{
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
		[DiagnosticDescriptors.RawIntegrationRegistration];

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		context.RegisterCompilationStartAction(startContext =>
		{
			var compilation = startContext.Compilation;
			var integrationType = compilation.GetTypeByMetadataName(WellKnownTypeNames.PluginIntegration);
			var addExtensionsType =
				compilation.GetTypeByMetadataName(WellKnownTypeNames.ServiceCollectionServiceExtensions);

			if (integrationType is null || addExtensionsType is null)
			{
				return;
			}

			startContext.RegisterOperationAction(operationContext =>
				{
					var invocation = (IInvocationOperation)operationContext.Operation;
					var method = invocation.TargetMethod;

					if (method.Name != "AddSingleton" ||
						!SymbolEqualityComparer.Default.Equals(method.ContainingType, addExtensionsType))
					{
						return;
					}

					var offender = method.TypeArguments.Length switch
					{
						// AddSingleton<T>(): T registering itself. Excludes T being IPluginIntegration itself,
						// which is exactly the shape RegisterIntegration's own factory overload uses.
						1 when !SymbolEqualityComparer.Default.Equals(method.TypeArguments[0], integrationType) &&
							method.TypeArguments[0].ImplementsInterface(integrationType)
							=> method.TypeArguments[0],

						// AddSingleton<IPluginIntegration, T>(): the service type is IPluginIntegration itself.
						2 when SymbolEqualityComparer.Default.Equals(method.TypeArguments[0], integrationType)
							=> method.TypeArguments[1],

						_ => null
					};

					if (offender is null)
					{
						return;
					}

					operationContext.ReportDiagnostic(Diagnostic.Create(
						DiagnosticDescriptors.RawIntegrationRegistration,
						invocation.Syntax.GetLocation(),
						offender.Name));
				},
				OperationKind.Invocation);
		});
	}
}
