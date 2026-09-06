using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace MacroDeck.Plugin.Analyzers.Rules;

/// <summary>
/// MDP1002: a constant declared local id - an action's <c>Id</c>, or a <c>DeclaredCapability.LocalId</c>
/// - that fails the declared-local-id rule (lowercase kebab, bounded length, no <c>::</c>).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DeclaredLocalIdGrammarAnalyzer : DiagnosticAnalyzer
{
	private const string ActionLabel = "Action";
	private const string CapabilityLabel = "Capability";

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
		[DiagnosticDescriptors.InvalidDeclaredLocalId];

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		context.RegisterCompilationStartAction(startContext =>
		{
			var compilation = startContext.Compilation;
			var actionDefinitionType = compilation.GetTypeByMetadataName(WellKnownTypeNames.ActionDefinition);
			var declaredCapabilityType = compilation.GetTypeByMetadataName(WellKnownTypeNames.DeclaredCapability);

			var actionIdProperty = actionDefinitionType?.GetMembers("Id").OfType<IPropertySymbol>().FirstOrDefault();

			if (actionDefinitionType is not null && actionIdProperty is not null)
			{
				startContext.RegisterSyntaxNodeAction(
					nodeContext => AnalyzeActionIdProperty(nodeContext, actionIdProperty),
					SyntaxKind.PropertyDeclaration);
			}

			if (declaredCapabilityType is not null)
			{
				startContext.RegisterOperationAction(
					operationContext => AnalyzeDeclaredCapabilityCreation(operationContext, declaredCapabilityType),
					OperationKind.ObjectCreation);
			}
		});
	}

	private static void AnalyzeActionIdProperty(SyntaxNodeAnalysisContext context, IPropertySymbol actionIdProperty)
	{
		var propertySyntax = (PropertyDeclarationSyntax)context.Node;

		// Cheap syntactic filter before paying for a declared-symbol lookup.
		if (propertySyntax.Identifier.ValueText != actionIdProperty.Name)
		{
			return;
		}

		if (context.SemanticModel.GetDeclaredSymbol(propertySyntax, context.CancellationToken) is not IPropertySymbol
			propertySymbol)
		{
			return;
		}

		var implementation = propertySymbol.ContainingType.FindImplementation(actionIdProperty);
		if (!SymbolEqualityComparer.Default.Equals(implementation, propertySymbol))
		{
			// Some other property that happens to be named "Id" - not the one IActionDefinition declares.
			return;
		}

		var id = PropertyConstantHelper.GetConstantStringValueOrNull(propertySyntax,
			context.SemanticModel,
			context.CancellationToken);

		if (id is null || IdentityGrammar.TryValidateDeclaredLocalId(id, out var error))
		{
			return;
		}

		var expression = PropertyConstantHelper.GetReturnedExpression(propertySyntax);

		context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.InvalidDeclaredLocalId,
			(expression ?? (SyntaxNode)propertySyntax).GetLocation(),
			ActionLabel,
			id,
			error));
	}

	private static void AnalyzeDeclaredCapabilityCreation(
		OperationAnalysisContext context,
		INamedTypeSymbol declaredCapabilityType)
	{
		var creation = (IObjectCreationOperation)context.Operation;

		if (!SymbolEqualityComparer.Default.Equals(creation.Type, declaredCapabilityType))
		{
			return;
		}

		var localIdAssignment = creation.Initializer?.Initializers
			.OfType<ISimpleAssignmentOperation>()
			.FirstOrDefault(assignment
				=> assignment.Target is IPropertyReferenceOperation { Property.Name: "LocalId" });

		if (localIdAssignment is null)
		{
			return;
		}

		var id = localIdAssignment.Value.GetConstantStringValueOrNull();
		if (id is null || IdentityGrammar.TryValidateDeclaredLocalId(id, out var error))
		{
			return;
		}

		context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.InvalidDeclaredLocalId,
			localIdAssignment.Value.Syntax.GetLocation(),
			CapabilityLabel,
			id,
			error));
	}
}
