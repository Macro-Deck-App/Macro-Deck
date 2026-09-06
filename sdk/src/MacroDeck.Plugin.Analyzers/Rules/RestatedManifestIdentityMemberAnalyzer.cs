using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MacroDeck.Plugin.Analyzers.Rules;

/// <summary>
/// MDP1004: a type implementing <c>IPluginIntegration</c> restates identity <c>manifest.json</c> already
/// owns - a public string <c>Id</c>/<c>Name</c>/<c>Version</c>, a public bool <c>IsInitialized</c>, or an
/// <c>IIntegrationIconProvider</c> implementation - none of which anything out of process ever reads from
/// the type itself.
///
/// <para>
/// The <c>IPluginIntegration</c> constraint is the false-positive fence and is not optional: a plugin
/// type legitimately owning, say, <c>public Device Name { get; }</c> (a non-string type) or
/// <c>public int Version { get; }</c> (a non-string type) is not restating manifest data and must not
/// fire, and a type that does not implement <c>IPluginIntegration</c> at all must never fire regardless of
/// what it declares - in particular every in-process <c>IIntegration</c> implementation under
/// <c>host/src</c> declares exactly these four members and legitimately implements
/// <c>IIntegrationIconProvider</c>, and none of that is this rule's business. Matching is by name and
/// declared type only (<c>string</c> for the first three, <c>bool</c> for the fourth) - a property with a
/// matching name but a different type never restates identity data, since the host could not have read it
/// as one even before <c>IPluginIntegration</c> existed.
/// </para>
///
/// <para>
/// Only checks members declared on the type itself: <see cref="ITypeSymbol.GetMembers()" /> (no
/// predicate) already excludes anything inherited, so a base class's own <c>Id</c> property does not make
/// every derived <c>IPluginIntegration</c> type fire for a member it never declared.
/// </para>
///
/// <para>
/// Syntax-node scoped (one type declaration at a time), not symbol-scoped: a node action's own
/// <see cref="SyntaxNodeAnalysisContext.SemanticModel" /> is the one a caller is supposed to use inside an
/// analyzer, whereas explicitly calling <see cref="Compilation.GetSemanticModel(SyntaxTree)" /> from a
/// symbol action (RS1030) risks a model that does not match what the host environment already built for
/// that tree.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RestatedManifestIdentityMemberAnalyzer : DiagnosticAnalyzer
{
	/// <summary>The four manifest-owned members this rule looks for, each paired with the declared type
	/// that makes it a restatement rather than a same-named, unrelated member.</summary>
	private static readonly (string Name, SpecialType Type)[] _restatedMembers =
	[
		("Id", SpecialType.System_String),
		("Name", SpecialType.System_String),
		("Version", SpecialType.System_String),
		("IsInitialized", SpecialType.System_Boolean)
	];

	/// <summary>
	/// <c>Name</c> after it stopped being a <c>string</c>. Matching on the declared type is what keeps this
	/// rule off an unrelated member that merely shares a name, so the localized shape has to be listed
	/// explicitly rather than the type check dropped.
	/// </summary>
	private static bool IsRestatedName(IPropertySymbol property)
		=> property.Name == "Name" &&
			property.Type.ToDisplayString() == WellKnownTypeNames.LocalizedTextDisplay;

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
		[DiagnosticDescriptors.RestatedManifestIdentityMember];

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		context.RegisterCompilationStartAction(startContext =>
		{
			var compilation = startContext.Compilation;
			var pluginIntegrationType = compilation.GetTypeByMetadataName(WellKnownTypeNames.PluginIntegration);
			var iconProviderType = compilation.GetTypeByMetadataName(WellKnownTypeNames.IntegrationIconProvider);

			if (pluginIntegrationType is null)
			{
				return;
			}

			startContext.RegisterSyntaxNodeAction(
				nodeContext => Analyze(nodeContext, pluginIntegrationType, iconProviderType),
				SyntaxKind.ClassDeclaration,
				SyntaxKind.StructDeclaration,
				SyntaxKind.RecordDeclaration,
				SyntaxKind.RecordStructDeclaration);
		});
	}

	private static void Analyze(
		SyntaxNodeAnalysisContext nodeContext,
		INamedTypeSymbol pluginIntegrationType,
		INamedTypeSymbol? iconProviderType)
	{
		var declaration = (TypeDeclarationSyntax)nodeContext.Node;

		if (nodeContext.SemanticModel.GetDeclaredSymbol(declaration, nodeContext.CancellationToken) is not
				INamedTypeSymbol type ||
			!type.ImplementsInterface(pluginIntegrationType))
		{
			return;
		}

		ReportRestatedMembers(nodeContext, declaration, type);
		ReportIconProviderImplementation(nodeContext, declaration, type, iconProviderType);
	}

	private static void ReportRestatedMembers(
		SyntaxNodeAnalysisContext nodeContext,
		TypeDeclarationSyntax declaration,
		INamedTypeSymbol type)
	{
		foreach (var member in type.GetMembers())
		{
			if (member is not IPropertySymbol { DeclaredAccessibility: Accessibility.Public } property)
			{
				continue;
			}

			// A partial type has one symbol but several declarations, and GetMembers() returns the merged
			// member list for each of them - reporting without this would raise one diagnostic per part
			// for a member declared exactly once.
			if (!IsDeclaredIn(property, declaration))
			{
				continue;
			}

			var isRestated = _restatedMembers.Any(candidate =>
					candidate.Name == property.Name && property.Type.SpecialType == candidate.Type) ||
				IsRestatedName(property);

			if (!isRestated)
			{
				continue;
			}

			nodeContext.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.RestatedManifestIdentityMember,
				property.Locations.FirstOrDefault() ?? type.Locations.FirstOrDefault(),
				$"'{type.Name}.{property.Name}' restates identity manifest.json already declares. " +
				"Nothing out of process reads this property - delete it."));
		}
	}

	private static void ReportIconProviderImplementation(
		SyntaxNodeAnalysisContext nodeContext,
		TypeDeclarationSyntax declaration,
		INamedTypeSymbol type,
		INamedTypeSymbol? iconProviderType)
	{
		if (iconProviderType is null || !type.ImplementsInterface(iconProviderType))
		{
			return;
		}

		// Implementing an interface is a property of the type, not of one part of it, so only the first
		// declaration reports - otherwise a partial type raises one diagnostic per part for a single
		// implementation. See ReportRestatedMembers' remark on partial types.
		if (!IsFirstDeclarationOf(type, declaration))
		{
			return;
		}

		// The base-list entry when this part names the interface itself, so the squiggle sits on the
		// interface reference; the type name when it is inherited from a base class and no entry exists.
		var location = FindBaseListLocation(nodeContext.SemanticModel, declaration, iconProviderType) ??
			declaration.Identifier.GetLocation();

		nodeContext.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.RestatedManifestIdentityMember,
			location,
			$"'{type.Name}' implements IIntegrationIconProvider. An out-of-process plugin's icon comes " +
			"from manifest.json's \"icon\" path - delete the icon-provider implementation."));
	}

	/// <summary>Whether <paramref name="property" /> is written inside <paramref name="declaration" />
	/// itself, rather than in another part of the same partial type.</summary>
	private static bool IsDeclaredIn(IPropertySymbol property, TypeDeclarationSyntax declaration)
		=> property.DeclaringSyntaxReferences.Any(reference =>
			reference.SyntaxTree == declaration.SyntaxTree && declaration.Span.Contains(reference.Span));

	/// <summary>Whether <paramref name="declaration" /> is the first of <paramref name="type" />'s parts,
	/// so that a type-level diagnostic is raised once rather than once per part.</summary>
	private static bool IsFirstDeclarationOf(INamedTypeSymbol type, TypeDeclarationSyntax declaration)
	{
		var first = type.DeclaringSyntaxReferences.FirstOrDefault();

		return first is not null &&
			first.SyntaxTree == declaration.SyntaxTree &&
			first.Span == declaration.Span;
	}

	/// <summary>The exact base-list entry that names <paramref name="interfaceType" /> on
	/// <paramref name="declaration" /> itself (not a base class further up the chain, which would have no
	/// entry in this declaration's own base list), so the diagnostic squiggles that interface reference
	/// rather than the whole type declaration.</summary>
	private static Location? FindBaseListLocation(
		SemanticModel semanticModel,
		TypeDeclarationSyntax declaration,
		INamedTypeSymbol interfaceType)
	{
		if (declaration.BaseList is null)
		{
			return null;
		}

		foreach (var baseType in declaration.BaseList.Types)
		{
			var symbol = semanticModel.GetTypeInfo(baseType.Type).Type;

			if (SymbolEqualityComparer.Default.Equals(symbol, interfaceType))
			{
				return baseType.GetLocation();
			}
		}

		return null;
	}
}
