using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MacroDeck.Plugin.Analyzers.Rules;

/// <summary>
/// MDP1003: <c>manifest.json</c> declares an <c>icon</c> whose file extension is not one
/// <see cref="IconMediaTypes" /> knows a media type for.
///
/// <para>
/// Modeled closely on <see cref="ManifestIdentityAnalyzer" />: reads <c>manifest.json</c> as an
/// <c>AdditionalFile</c> (the package's own <c>build/MacroDeck.Plugin.Analyzers.props</c> adds it
/// automatically, no opt-in needed), uses <see cref="ManifestJsonReader" /> to find the <c>icon</c>
/// key's own quoted literal, and squiggles exactly that literal. An absent <c>icon</c> key is legal -
/// not every plugin ships an icon - so it is left alone; only a present one with an extension
/// <see cref="IconMediaTypes.ForExtension" /> cannot answer is flagged. The decision and the list of
/// supported extensions both come from <see cref="IconMediaTypes" />, linked in from
/// <c>MacroDeck.Plugin.Protocol</c> rather than duplicated - see this project's own .csproj comment on
/// that link for why the analyzer cannot reference that assembly directly.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnsupportedManifestIconExtensionAnalyzer : DiagnosticAnalyzer
{
	private const string ManifestFileName = "manifest.json";

	private static readonly string[] _iconKey = ["icon"];

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
		[DiagnosticDescriptors.UnsupportedManifestIconExtension];

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterAdditionalFileAction(AnalyzeManifest);
	}

	private static void AnalyzeManifest(AdditionalFileAnalysisContext context)
	{
		if (!string.Equals(Path.GetFileName(context.AdditionalFile.Path),
			ManifestFileName,
			StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		var text = context.AdditionalFile.GetText(context.CancellationToken);
		if (text is null)
		{
			return;
		}

		var properties = ManifestJsonReader.ReadTopLevelStrings(text.ToString(), _iconKey);

		if (!properties.TryGetValue("icon", out var icon))
		{
			// No icon declared at all - legal, and none of this rule's business. A missing icon is not
			// "unsupported"; it is simply absent.
			return;
		}

		if (IconMediaTypes.ForExtension(icon.Value) is not null)
		{
			return;
		}

		var supported = string.Join(", ", IconMediaTypes.SupportedExtensions);
		var location = Location.Create(context.AdditionalFile.Path,
			icon.ValueSpan,
			text.Lines.GetLinePositionSpan(icon.ValueSpan));

		context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.UnsupportedManifestIconExtension,
			location,
			$"manifest.json declares icon '{icon.Value}', whose extension has no known media type. " +
			$"Supported extensions are: {supported}."));
	}
}
