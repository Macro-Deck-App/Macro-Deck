using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MacroDeck.Plugin.Analyzers.Rules;

/// <summary>MDP3003: <c>async void</c> on any member of a type implementing an SDK contract (see
/// <see cref="SdkContractTypeNames" />).</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AsyncVoidMemberAnalyzer : DiagnosticAnalyzer
{
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
		[DiagnosticDescriptors.AsyncVoidMember];

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		context.RegisterCompilationStartAction(startContext =>
		{
			var compilation = startContext.Compilation;
			var eventArgsType = compilation.GetTypeByMetadataName(WellKnownTypeNames.EventArgs);

			var contractTypes = SdkContractTypeNames.All
				.Select(compilation.GetTypeByMetadataName)
				.Where(type => type is not null)
				.Cast<INamedTypeSymbol>()
				.ToImmutableArray();

			if (contractTypes.IsEmpty)
			{
				return;
			}

			startContext.RegisterSymbolAction(symbolContext => Analyze(symbolContext, contractTypes, eventArgsType),
				SymbolKind.Method);
		});
	}

	private static void Analyze(
		SymbolAnalysisContext context,
		ImmutableArray<INamedTypeSymbol> contractTypes,
		INamedTypeSymbol? eventArgsType)
	{
		var method = (IMethodSymbol)context.Symbol;

		if (!method.IsAsync || !method.ReturnsVoid || method.MethodKind != MethodKind.Ordinary)
		{
			return;
		}

		if (IsStandardEventHandlerSignature(method, eventArgsType))
		{
			return;
		}

		var implementsContract = false;
		foreach (var contract in contractTypes)
		{
			if (method.ContainingType.IsOrImplements(contract))
			{
				implementsContract = true;
				break;
			}
		}

		if (!implementsContract)
		{
			return;
		}

		var location = method.Locations.FirstOrDefault();
		if (location is null)
		{
			return;
		}

		context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.AsyncVoidMember, location, method.Name));
	}

	/// <summary>The one legitimate use of async void - a method shaped like a standard .NET event
	/// handler, <c>(object? sender, TEventArgs e)</c>.</summary>
	private static bool IsStandardEventHandlerSignature(IMethodSymbol method, INamedTypeSymbol? eventArgsType)
		=> method.Parameters.Length == 2 && method.Parameters[1].Type.IsOrDerivesFrom(eventArgsType);
}
