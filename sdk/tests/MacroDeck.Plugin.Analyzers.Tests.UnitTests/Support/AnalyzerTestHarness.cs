using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace MacroDeck.Plugin.Analyzers.Tests.UnitTests.Support;

/// <summary>
/// Compiles a source string against the real MacroDeck.Sdk/MacroDeck.Plugin.Hosting/
/// MacroDeck.Plugin.Protocol assemblies and runs one analyzer over it - a hand-rolled stand-in for
/// Microsoft.CodeAnalysis.Testing, which would pin its own, separately-versioned copies of the Roslyn
/// packages this project already references directly.
/// </summary>
internal static class AnalyzerTestHarness
{
	// TRUSTED_PLATFORM_ASSEMBLIES lists every assembly the running test process resolved, which - because
	// this project's own ProjectReferences pull in the ASP.NET Core shared framework transitively -
	// already includes everything a plugin snippet's source could reference: the BCL, ASP.NET Core and
	// Microsoft.Extensions.*.
	private static readonly Lazy<ImmutableArray<MetadataReference>> _platformReferences = new(() =>
	[
		.. ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
		.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
		.Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
	]);

	private static readonly ImmutableArray<MetadataReference> _sdkReferences =
	[
		MetadataReference.CreateFromFile(typeof(Sdk.IIntegration).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(Protocol.Handshake.CapabilityKinds).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(Hosting.PluginHostBuilder).Assembly.Location)
	];

	/// <summary>Every assembly the running test process resolved - exposed for
	/// <see cref="GeneratorTestHarness" />, which builds its own compilations the same way this class
	/// does but drives a source generator instead of an analyzer.</summary>
	internal static ImmutableArray<MetadataReference> PlatformReferences => _platformReferences.Value;

	/// <summary>The real MacroDeck.Sdk/MacroDeck.Plugin.Protocol/MacroDeck.Plugin.Hosting assemblies -
	/// exposed for <see cref="GeneratorTestHarness" />.</summary>
	internal static ImmutableArray<MetadataReference> SdkReferences => _sdkReferences;

	/// <summary>The SDK assembly's real name, read off the assembly rather than spelled out - a test that
	/// hardcodes it would move in lockstep with the analyzer constant it is meant to be checking.</summary>
	internal static string SdkAssemblyName { get; } = typeof(Sdk.IIntegration).Assembly.GetName().Name!;

	/// <summary>
	/// Compiles <paramref name="source" /> and runs <paramref name="analyzer" /> over it, returning only
	/// that analyzer's own diagnostics. Fails the test immediately, with the compiler's own errors, if
	/// the snippet does not compile - a positive or a near-miss snippet that fails to compile is a broken
	/// test, not a passing or failing one.
	/// </summary>
	/// <param name="assemblyName">
	/// The compilation's own assembly name. Defaults to a name that is deliberately not one of the
	/// SDK assembly names MDP5001 tracks; pass one of those names to test that rule, since an analyzer
	/// never sees the assembly it ships in, only the assembly identity of whatever it is analyzing.
	/// </param>
	public static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(
		string source,
		DiagnosticAnalyzer analyzer,
		string assemblyName = "PluginUnderTest")
	{
		var compilation = CSharpCompilation.Create(assemblyName,
			[CSharpSyntaxTree.ParseText(source)],
			_platformReferences.Value.AddRange(_sdkReferences),
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		var compileErrors = compilation.GetDiagnostics()
			.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
			.ToList();

		if (compileErrors.Count > 0)
		{
			Assert.Fail("Test source does not compile:" +
				Environment.NewLine +
				string.Join(Environment.NewLine, compileErrors));
		}

		// Only this one analyzer is registered, so GetAnalyzerDiagnosticsAsync already returns only its
		// diagnostics - no filtering needed.
		var withAnalyzers = compilation.WithAnalyzers([analyzer]);
		return await withAnalyzers.GetAnalyzerDiagnosticsAsync();
	}

	/// <summary>
	/// Runs <paramref name="analyzer" /> with <paramref name="fileName" />/<paramref name="fileContent" />
	/// as its only <c>AdditionalFiles</c> entry, over an otherwise-empty compilation - for MDP4002's
	/// <c>launchSettings.json</c> half, which <see cref="AnalysisContext.RegisterAdditionalFileAction" />
	/// reaches independently of any C# source.
	/// </summary>
	public static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsFromAdditionalFileAsync(
		string fileName,
		string fileContent,
		DiagnosticAnalyzer analyzer)
	{
		var compilation = CSharpCompilation.Create("PluginUnderTest",
			[CSharpSyntaxTree.ParseText("internal static class Empty { }")],
			_platformReferences.Value.AddRange(_sdkReferences),
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		var options = new AnalyzerOptions([new StaticAdditionalText(fileName, fileContent)]);
		var withAnalyzers = compilation.WithAnalyzers([analyzer], options);
		return await withAnalyzers.GetAnalyzerDiagnosticsAsync();
	}

	/// <summary>The exact source text a diagnostic's location squiggles - what a reader would see
	/// highlighted, and the most refactor-resistant way for a test to assert "the right location".</summary>
	public static string GetSquiggleText(Diagnostic diagnostic, string source)
		=> source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length);

	/// <summary>The minimal <see cref="AdditionalText" /> implementation the real MSBuild/IDE hosts
	/// provide, standing in for one here.</summary>
	private sealed class StaticAdditionalText(string path, string content) : AdditionalText
	{
		public override string Path { get; } = path;

		public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(content);
	}
}
