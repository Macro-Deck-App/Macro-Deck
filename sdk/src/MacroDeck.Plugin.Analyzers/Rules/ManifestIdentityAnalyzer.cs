using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace MacroDeck.Plugin.Analyzers.Rules;

/// <summary>
/// MDP1001: <c>manifest.json</c> declares an <c>id</c> that fails the package owner-id grammar, or
/// declares no <c>name</c> or no <c>version</c> at all.
///
/// <para>
/// Reads <c>manifest.json</c> as an <c>AdditionalFile</c> rather than looking for a builder call - since
/// #522, a plugin's <c>id</c>, <c>name</c> and <c>version</c> come from the manifest, not from
/// <c>PluginHostBuilder</c>. The package's own <c>build/MacroDeck.Plugin.Analyzers.props</c> adds
/// <c>manifest.json</c> to <c>AdditionalFiles</c> automatically, so a consuming project needs no opt-in
/// for this rule to see it. All three fields are exactly what <c>PluginHostBuilder.Build</c> requires at
/// runtime now (an invalid id, an absent name, an absent version each become a problem in
/// <c>PluginConfigurationException</c>) - this rule catches all three failures at compile time instead.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ManifestIdentityAnalyzer : DiagnosticAnalyzer
{
	private const string ManifestFileName = "manifest.json";

	private static readonly string[] _requiredKeys = ["id", "name", "version"];

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
		[DiagnosticDescriptors.InvalidManifestIdentity];

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

		var properties = ManifestJsonReader.ReadTopLevelStrings(text.ToString(), _requiredKeys);

		// Where to squiggle when a field is missing outright, so there is no specific span in the
		// document to point at - the first line is the closest thing to "somewhere in here" a reader can
		// jump straight to.
		var fallback = text.Lines.Count > 0 ? text.Lines[0].Span : new TextSpan(0, 0);

		CheckId(context, text, properties, fallback);
		CheckNonEmpty(context, text, properties, fallback, "name");
		CheckNonEmpty(context, text, properties, fallback, "version");
	}

	private static void CheckId(
		AdditionalFileAnalysisContext context,
		SourceText text,
		IReadOnlyDictionary<string, ManifestJsonReader.ManifestStringProperty> properties,
		TextSpan fallback)
	{
		if (!properties.TryGetValue("id", out var id))
		{
			Report(context,
				text,
				fallback,
				"manifest.json declares no \"id\", or it is not a string. Set \"id\" to a reverse-domain " +
				"value such as 'com.example.my-plugin'.");
			return;
		}

		if (!IdentityGrammar.TryValidatePackageOwnerId(id.Value, out var error))
		{
			Report(context,
				text,
				id.ValueSpan,
				$"manifest.json declares id '{id.Value}', which is not usable. {error} Set \"id\" to a " +
				"reverse-domain value such as 'com.example.my-plugin'.");
		}
	}

	/// <summary><paramref name="key" /> must be present and a non-empty string - the shape
	/// <c>name</c> and <c>version</c> both require, with no grammar beyond that.</summary>
	private static void CheckNonEmpty(
		AdditionalFileAnalysisContext context,
		SourceText text,
		IReadOnlyDictionary<string, ManifestJsonReader.ManifestStringProperty> properties,
		TextSpan fallback,
		string key)
	{
		var found = properties.TryGetValue(key, out var property);

		if (found && property.Value.Length > 0)
		{
			return;
		}

		Report(context,
			text,
			found ? property.ValueSpan : fallback,
			$"manifest.json declares no \"{key}\", or an empty one. PluginHostBuilder.Build() requires a " +
			$"non-empty {key}.");
	}

	private static void Report(AdditionalFileAnalysisContext context, SourceText text, TextSpan span, string message)
	{
		var location = Location.Create(context.AdditionalFile.Path, span, text.Lines.GetLinePositionSpan(span));
		context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.InvalidManifestIdentity, location, message));
	}
}
