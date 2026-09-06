using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MacroDeck.Plugin.Analyzers.Rules;

/// <summary>MDP2002: <c>ICapabilityHandler.Kind</c> returning a constant outside the known capability
/// kinds.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnknownCapabilityKindAnalyzer : DiagnosticAnalyzer
{
	private static readonly string _knownKinds = string.Join(", ", CapabilityKindVocabulary.All);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
		[DiagnosticDescriptors.UnknownCapabilityKind];

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		context.RegisterCompilationStartAction(startContext =>
		{
			var handlerType = startContext.Compilation.GetTypeByMetadataName(WellKnownTypeNames.CapabilityHandler);
			var kindProperty = handlerType?.GetMembers("Kind").OfType<IPropertySymbol>().FirstOrDefault();

			if (kindProperty is null)
			{
				return;
			}

			startContext.RegisterSyntaxNodeAction(nodeContext => Analyze(nodeContext, kindProperty),
				SyntaxKind.PropertyDeclaration);
		});
	}

	private static void Analyze(SyntaxNodeAnalysisContext context, IPropertySymbol kindProperty)
	{
		var propertySyntax = (PropertyDeclarationSyntax)context.Node;

		if (propertySyntax.Identifier.ValueText != kindProperty.Name)
		{
			return;
		}

		if (context.SemanticModel.GetDeclaredSymbol(propertySyntax, context.CancellationToken) is not IPropertySymbol
			propertySymbol)
		{
			return;
		}

		var implementation = propertySymbol.ContainingType.FindImplementation(kindProperty);
		if (!SymbolEqualityComparer.Default.Equals(implementation, propertySymbol))
		{
			return;
		}

		var kind = PropertyConstantHelper.GetConstantStringValueOrNull(propertySyntax,
			context.SemanticModel,
			context.CancellationToken);

		if (kind is null || CapabilityKindVocabulary.IsKnown(kind))
		{
			return;
		}

		var expression = PropertyConstantHelper.GetReturnedExpression(propertySyntax);

		context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.UnknownCapabilityKind,
			(expression ?? (SyntaxNode)propertySyntax).GetLocation(),
			kind,
			_knownKinds));
	}
}
