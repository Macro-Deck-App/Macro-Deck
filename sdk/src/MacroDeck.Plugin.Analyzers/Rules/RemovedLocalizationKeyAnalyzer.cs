using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace MacroDeck.Plugin.Analyzers.Rules;

/// <summary>
/// MDLOC006: use of a Macro Deck localization key that has been retired.
///
/// <para>
/// A retired key stays in the catalog and stays generated, so existing code keeps compiling and keeps
/// rendering text rather than turning into a bare "member does not exist" error with nothing to migrate
/// to. This rule is what makes the retirement visible, at the call site, with the replacement to move to.
/// </para>
///
/// <para>
/// Data-driven, like <see cref="DeprecatedSdkApiAnalyzer" />: it reads the marker attribute's own value
/// out of metadata, so retiring a key needs no change here - only a <c>[removed:…]</c> prefix on that
/// resource's comment.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RemovedLocalizationKeyAnalyzer : DiagnosticAnalyzer
{
	private const string _attributeMetadataName =
		"MacroDeck.Localization.MacroDeckLocalizationRemovedAttribute";

	/// <inheritdoc />
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
		[DiagnosticDescriptors.RemovedMacroDeckLocalizationKey];

	/// <inheritdoc />
	public override void Initialize(AnalysisContext context)
	{
		if (context is null)
		{
			return;
		}

		// The declaration this rule keys off is itself generated; the call sites it reports are not.
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		context.RegisterCompilationStartAction(start =>
		{
			var marker = start.Compilation.GetTypeByMetadataName(_attributeMetadataName);

			if (marker is null)
			{
				return;
			}

			start.RegisterOperationAction(operation => Analyze(operation, marker), OperationKind.Invocation);
		});
	}

	private static void Analyze(OperationAnalysisContext context, INamedTypeSymbol marker)
	{
		if (context.Operation is not IInvocationOperation invocation)
		{
			return;
		}

		var guidance = GuidanceOf(invocation.TargetMethod, marker);

		if (guidance is null)
		{
			return;
		}

		context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.RemovedMacroDeckLocalizationKey,
			invocation.Syntax.GetLocation(),
			$"'{invocation.TargetMethod.Name}' names a Macro Deck localization key that has been removed. {guidance}"));
	}

	private static string? GuidanceOf(ISymbol symbol, INamedTypeSymbol marker)
	{
		foreach (var attribute in symbol.GetAttributes())
		{
			if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, marker))
			{
				continue;
			}

			return attribute.ConstructorArguments.Length > 0 &&
				attribute.ConstructorArguments[0].Value is string guidance &&
				guidance.Length > 0
					? guidance
					: "Use a current key instead.";
		}

		return null;
	}
}
