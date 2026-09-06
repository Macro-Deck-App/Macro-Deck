using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace MacroDeck.Plugin.Analyzers.Tests.UnitTests.Support;

/// <summary>
/// Runs <see cref="SdkUsageManifestGenerator" /> over a compilation built the same way
/// <see cref="AnalyzerTestHarness" /> builds one for an analyzer, so the two harnesses stay consistent
/// about what "the real SDK assemblies" and "the platform" mean.
/// </summary>
internal static class GeneratorTestHarness
{
	/// <summary>
	/// Compiles <paramref name="source" /> - referencing the real SDK assemblies when
	/// <paramref name="referenceSdk" /> is true, or only the BCL/ASP.NET Core platform otherwise - then
	/// runs the generator over it and returns the text of the <c>MacroDeckSdkUsage.g.cs</c> source it
	/// produced, or null if it produced none.
	/// </summary>
	public static string? RunAndGetGeneratedSource(string source, string assemblyName, bool referenceSdk)
	{
		// This test project's own ProjectReference to MacroDeck.Sdk means MacroDeck.Sdk.dll is already
		// loaded in the running test process, so it shows up in AnalyzerTestHarness.PlatformReferences
		// (built from TRUSTED_PLATFORM_ASSEMBLIES) even when the caller asks for a compilation that does
		// not reference the SDK. It has to be filtered back out explicitly to build that compilation.
		var references = referenceSdk
			? AnalyzerTestHarness.PlatformReferences.AddRange(AnalyzerTestHarness.SdkReferences)
			: AnalyzerTestHarness.PlatformReferences.RemoveAll(reference =>
				Path.GetFileNameWithoutExtension(reference.Display) == "MacroDeck.Sdk");

		var compilation = CSharpCompilation.Create(assemblyName,
			[CSharpSyntaxTree.ParseText(source)],
			references,
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

		var driver = CSharpGeneratorDriver.Create(new SdkUsageManifestGenerator());
		var updatedDriver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

		var runResult = updatedDriver.GetRunResult();

		return runResult.Results
			.SelectMany(result => result.GeneratedSources)
			.FirstOrDefault(generated => generated.HintName == "MacroDeckSdkUsage.g.cs")
			.SourceText
			?.ToString();
	}
}
