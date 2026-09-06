using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MacroDeck.Plugin.Analyzers.Rules;

/// <summary>
/// MDP5003: a <c>[MacroDeckDeprecated]</c> declaration that cannot be acted on.
///
/// <para>
/// This fires where a deprecation is <em>declared</em>, not where one is used - so in practice it runs
/// against the SDK's own source, and against any plugin that chooses to deprecate its own surface with
/// the same attribute. It is the compile-time half of the lifecycle rule that
/// <c>SdkDeprecationLifecycleTests</c> enforces at test time: an author cannot half-declare a
/// deprecation and discover only later that the host had nothing to show a user.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DeprecationMetadataAnalyzer : DiagnosticAnalyzer
{
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
		[DiagnosticDescriptors.IncompleteDeprecationMetadata];

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		context.RegisterCompilationStartAction(startContext =>
		{
			if (startContext.Compilation.GetTypeByMetadataName(WellKnownTypeNames.MacroDeckDeprecatedAttribute)
				is null)
			{
				return;
			}

			startContext.RegisterSymbolAction(Analyze,
				SymbolKind.NamedType,
				SymbolKind.Method,
				SymbolKind.Property,
				SymbolKind.Event,
				SymbolKind.Field);
		});
	}

	private static void Analyze(SymbolAnalysisContext context)
	{
		var symbol = context.Symbol;

		if (!DeprecationMetadata.TryRead(symbol, out var metadata))
		{
			return;
		}

		var problems = Problems(symbol, metadata);

		if (problems.Count == 0)
		{
			return;
		}

		foreach (var location in symbol.Locations)
		{
			if (!location.IsInSource)
			{
				continue;
			}

			context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.IncompleteDeprecationMetadata,
				location,
				symbol.ToDisplayString(),
				string.Join(" ", problems)));
		}
	}

	private static List<string> Problems(ISymbol symbol, DeprecationMetadata metadata)
	{
		var problems = new List<string>();

		if (!HasObsoleteAttribute(symbol))
		{
			problems.Add("It carries no companion [Obsolete], so the compiler and every IDE stay silent " +
				"about it.");
		}

		if (string.IsNullOrWhiteSpace(metadata.Guidance))
		{
			problems.Add("Guidance is empty, so the warning tells a plugin author nothing to do.");
		}

		if (DeprecationMetadata.IsNotAfter(metadata.RemovedIn, metadata.DeprecatedIn))
		{
			problems.Add($"RemovedIn ('{metadata.RemovedIn}') is not after DeprecatedIn " +
				$"('{metadata.DeprecatedIn}'), which leaves no release to migrate on.");
		}

		return problems;
	}

	private static bool HasObsoleteAttribute(ISymbol symbol)
	{
		foreach (var attribute in symbol.GetAttributes())
		{
			if (attribute.AttributeClass?.ToDisplayString() == WellKnownTypeNames.ObsoleteAttribute)
			{
				return true;
			}
		}

		return false;
	}
}
