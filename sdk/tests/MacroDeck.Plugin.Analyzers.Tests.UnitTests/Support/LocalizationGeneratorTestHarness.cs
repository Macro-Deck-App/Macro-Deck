using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace MacroDeck.Plugin.Analyzers.Tests.UnitTests.Support;

/// <summary>
/// Runs <see cref="LocalizationGenerator" /> over a set of resource files, the way MSBuild would: the
/// .resx arrive as <see cref="AdditionalText" />, the scope and class name as compiler-visible build
/// properties. Reuses <see cref="AnalyzerTestHarness" />'s references so the generated source can be
/// compiled for real against MacroDeck.Localization rather than only inspected as text.
/// </summary>
internal static class LocalizationGeneratorTestHarness
{
	/// <summary>The default-language file name a resource set is built around.</summary>
	public const string DefaultFileName = "Strings.resx";

	/// <summary>What one generator run produced.</summary>
	internal sealed record GeneratorRun(string? GeneratedSource, ImmutableArray<Diagnostic> Diagnostics)
	{
		/// <summary>The diagnostic ids reported, in the order they were reported.</summary>
		public string[] Ids => [.. Diagnostics.Select(diagnostic => diagnostic.Id)];
	}

	/// <summary>Wraps <paramref name="entries" /> - raw <c>&lt;data&gt;</c> XML - in a resource document.</summary>
	public static string Resx(string entries)
		=> "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<root>\n" +
			"  <resheader name=\"resmimetype\"><value>text/microsoft-resx</value></resheader>\n" +
			"  <resheader name=\"version\"><value>2.0</value></resheader>\n" +
			entries +
			"\n</root>";

	/// <summary>One <c>&lt;data&gt;</c> entry.</summary>
	public static string Entry(string name, string value, string? comment = null)
		=> $"  <data name=\"{name}\" xml:space=\"preserve\">\n    <value>{value}</value>\n" +
			(comment is null ? string.Empty : $"    <comment>{comment}</comment>\n") +
			"  </data>";

	/// <summary>Runs the generator over <paramref name="files" />, keyed by file name.</summary>
	/// <param name="files">Resource file name to its full XML.</param>
	/// <param name="scope">The scope build property. Null leaves it unset, so the generator falls back to
	/// the plugin id in manifest.json.</param>
	/// <param name="manifestJson">A manifest.json to offer alongside the resources, or null for none.</param>
	public static GeneratorRun Run(IReadOnlyDictionary<string, string> files,
		string? scope = "macrodeck",
		string? manifestJson = null)
	{
		var additional = new List<AdditionalText>();

		foreach (var file in files)
		{
			additional.Add(new StaticText($"/project/Localization/{file.Key}", file.Value));
		}

		if (manifestJson is not null)
		{
			additional.Add(new StaticText("/project/manifest.json", manifestJson));
		}

		var options = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["build_property.MacroDeckLocalizationClassName"] = "Strings",
			["build_property.RootNamespace"] = "TestPlugin",
		};

		if (scope is not null)
		{
			options["build_property.MacroDeckLocalizationScope"] = scope;
		}

		var compilation = CSharpCompilation.Create("TestPlugin",
			[CSharpSyntaxTree.ParseText("namespace TestPlugin { internal static class Anchor { } }")],
			AnalyzerTestHarness.PlatformReferences,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		var driver = CSharpGeneratorDriver.Create([new LocalizationGenerator().AsSourceGenerator()],
			additional,
			optionsProvider: new StaticOptionsProvider(options));

		var result = driver.RunGenerators(compilation).GetRunResult().Results.Single();

		var source = result.GeneratedSources.Length == 0
			? null
			: result.GeneratedSources[0].SourceText.ToString();

		return new GeneratorRun(source, result.Diagnostics);
	}

	/// <summary>Compiles <paramref name="generatedSource" /> together with <paramref name="callSite" />
	/// against the real MacroDeck.Localization assembly and returns the compiler's own errors. This is how
	/// a test tells "a typed API was generated" apart from "some API was generated" - a stringly typed
	/// façade compiles any call, a typed one rejects the wrong arity and the wrong argument type.</summary>
	public static string[] CompileErrors(string generatedSource, string callSite)
	{
		var compilation = CSharpCompilation.Create("TestPluginCallSite",
			[CSharpSyntaxTree.ParseText(generatedSource), CSharpSyntaxTree.ParseText(callSite)],
			AnalyzerTestHarness.PlatformReferences.Add(
				MetadataReference.CreateFromFile(typeof(Localization.LocalizedString).Assembly.Location)),
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		return
		[
			.. compilation.GetDiagnostics()
				.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
				.Select(diagnostic => diagnostic.Id),
		];
	}

	/// <summary>Runs <paramref name="analyzer" /> over the generated source plus a call site, which is how
	/// a rule keyed off a generated declaration is exercised - the declaration and the usage have to be in
	/// one compilation.</summary>
	public static async Task<ImmutableArray<Diagnostic>> AnalyzeCallSiteAsync(string generatedSource,
		string callSite,
		DiagnosticAnalyzer analyzer)
	{
		var compilation = CSharpCompilation.Create("TestPluginCallSite",
			[CSharpSyntaxTree.ParseText(generatedSource), CSharpSyntaxTree.ParseText(callSite)],
			AnalyzerTestHarness.PlatformReferences.Add(
				MetadataReference.CreateFromFile(typeof(Localization.LocalizedString).Assembly.Location)),
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		var errors = compilation.GetDiagnostics()
			.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
			.ToList();

		if (errors.Count > 0)
		{
			Assert.Fail("Call-site source does not compile:" +
				Environment.NewLine +
				string.Join(Environment.NewLine, errors));
		}

		return await compilation.WithAnalyzers([analyzer]).GetAnalyzerDiagnosticsAsync();
	}

	private sealed class StaticText(string path, string content) : AdditionalText
	{
		public override string Path { get; } = path;

		public override SourceText GetText(CancellationToken cancellationToken = default)
			=> SourceText.From(content);
	}

	private sealed class StaticOptionsProvider(Dictionary<string, string> globals)
		: AnalyzerConfigOptionsProvider
	{
		public override AnalyzerConfigOptions GlobalOptions { get; } = new StaticOptions(globals);

		public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

		public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;
	}

	private sealed class StaticOptions(Dictionary<string, string> values) : AnalyzerConfigOptions
	{
		public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
	}
}
