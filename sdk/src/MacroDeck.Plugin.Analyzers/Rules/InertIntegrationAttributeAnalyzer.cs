using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MacroDeck.Plugin.Analyzers.Rules;

/// <summary>
/// MDP2006: <c>[MacroDeckIntegration]</c> applied to a type implementing <c>IPluginIntegration</c>.
///
/// <para>
/// <c>MacroDeckIntegrationAttribute</c> carries only <c>Platforms</c> and <c>EnabledByDefault</c> - no
/// identity - and both are inert on an out-of-process plugin: platform gating comes from
/// <c>manifest.json</c>'s <c>entrypoints</c> (the host launches whichever entrypoint names the current
/// platform, or none at all), and enabled-by-default is derived from whether the plugin declares a
/// config-flow capability, not read off this attribute. Nothing out of process ever inspects the
/// attribute on an <c>IPluginIntegration</c> type, so it is dead weight rather than a mistake that breaks
/// anything - which is why this is a <b>Warning</b>, not the Error severity this package reserves for "this
/// cannot work" (compare MDP1004, which shares the same manifest-is-the-source rationale but flags members
/// that would otherwise silently drift from the manifest, not an attribute that is simply never read).
/// </para>
///
/// <para>
/// Must not fire on an in-process <c>IIntegration</c> type: the attribute is exactly how the in-process
/// host discovers and gates those, so the same constraint style MDP1004 uses - implements
/// <c>IPluginIntegration</c>, not merely "carries the attribute" - is the false-positive fence here too.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InertIntegrationAttributeAnalyzer : DiagnosticAnalyzer
{
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
		[DiagnosticDescriptors.InertIntegrationAttribute];

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		context.RegisterCompilationStartAction(startContext =>
		{
			var compilation = startContext.Compilation;
			var pluginIntegrationType = compilation.GetTypeByMetadataName(WellKnownTypeNames.PluginIntegration);
			var attributeType =
				compilation.GetTypeByMetadataName(WellKnownTypeNames.MacroDeckIntegrationAttribute);

			if (pluginIntegrationType is null || attributeType is null)
			{
				return;
			}

			startContext.RegisterSymbolAction(symbolContext =>
				{
					var type = (INamedTypeSymbol)symbolContext.Symbol;

					if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct) ||
						!type.ImplementsInterface(pluginIntegrationType))
					{
						return;
					}

					var attribute = type.GetAttributes()
						.FirstOrDefault(candidate =>
							SymbolEqualityComparer.Default.Equals(candidate.AttributeClass, attributeType));

					if (attribute is null)
					{
						return;
					}

					var location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ??
						type.Locations.FirstOrDefault();

					symbolContext.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.InertIntegrationAttribute,
						location,
						type.Name));
				},
				SymbolKind.NamedType);
		});
	}
}
