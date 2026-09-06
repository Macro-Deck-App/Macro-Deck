using System.Reflection;
using MacroDeck.Plugin.Analyzers.Tests.UnitTests.Support;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace MacroDeck.Plugin.Analyzers.Tests.UnitTests;

/// <summary>
/// An analyzer never references the SDK as an assembly - it looks every type up by name against the
/// compilation it is handed, and <c>GetTypeByMetadataName</c> answers <c>null</c> for a name that no
/// longer exists. A stale entry in <see cref="WellKnownTypeNames" /> or <see cref="SdkContractTypeNames" />
/// therefore does not break the build: it makes the rule that depends on it silently stop firing, which
/// no other test in this project would notice. These tests hold the name tables against the real
/// MacroDeck assemblies so that a rename on the SDK side is a red test rather than an inert analyzer.
/// </summary>
[TestFixture]
public class SdkTypeNameResolutionTests
{
	/// <summary>
	/// Only the MacroDeck-owned names. The BCL and ASP.NET Core names in the same table are outside this
	/// repository's control and cannot go stale through a change made here.
	/// </summary>
	private static IEnumerable<string> MacroDeckTypeNames => typeof(WellKnownTypeNames)
		.GetFields(BindingFlags.Public | BindingFlags.Static)
		.Where(constant => constant.IsLiteral && constant.FieldType == typeof(string))
		.Select(constant => (string)constant.GetRawConstantValue()!)
		.Concat(SdkContractTypeNames.All)
		.Where(name => name.StartsWith("MacroDeck", StringComparison.Ordinal))
		.Distinct();

	[Test]
	public void Every_MacroDeck_type_name_an_analyzer_looks_up_resolves_against_the_real_assemblies()
	{
		var compilation = CSharpCompilation.Create("PluginUnderTest",
			[CSharpSyntaxTree.ParseText("internal static class Empty { }")],
			AnalyzerTestHarness.PlatformReferences.AddRange(AnalyzerTestHarness.SdkReferences),
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		var unresolved = MacroDeckTypeNames
			.Where(name => compilation.GetTypeByMetadataName(name) is null)
			.ToList();

		Assert.That(unresolved,
			Is.Empty,
			"these names no longer name a type in the MacroDeck assemblies, so every rule looking them up is inert");
	}

	/// <summary>
	/// The contract list is what MDP3002 and MDP3003 mean by "an SDK contract". An entry that is not an
	/// interface would be matched against an author's implemented-interface set and never hit, which is the
	/// same silent failure as a stale name wearing a different disguise.
	/// </summary>
	[Test]
	public void Every_SDK_contract_name_resolves_to_an_interface()
	{
		var compilation = CSharpCompilation.Create("PluginUnderTest",
			[CSharpSyntaxTree.ParseText("internal static class Empty { }")],
			AnalyzerTestHarness.PlatformReferences.AddRange(AnalyzerTestHarness.SdkReferences),
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		var notInterfaces = SdkContractTypeNames.All
			.Where(name => compilation.GetTypeByMetadataName(name)?.TypeKind != TypeKind.Interface)
			.ToList();

		Assert.That(notInterfaces, Is.Empty);
	}

	/// <summary>
	/// The one name the analyzers match as an assembly rather than a type. Read off the real assembly here
	/// so the assertion cannot be satisfied by a test constant that was renamed alongside the production
	/// one - the failure this guards against is exactly the two copies drifting apart.
	/// </summary>
	[Test]
	public void The_SDK_types_the_analyzers_track_come_from_the_assembly_they_track_by_name()
	{
		var sdkTypeNames = MacroDeckTypeNames
			.Where(name => name.StartsWith("MacroDeck.Sdk.", StringComparison.Ordinal))
			.ToList();

		Assert.That(sdkTypeNames, Is.Not.Empty, "the tables should still name types in the SDK itself");
		Assert.That(sdkTypeNames,
			Has.All.StartWith(AnalyzerTestHarness.SdkAssemblyName + "."),
			"the analyzers' SDK type names and the SDK assembly they gate on have to name the same thing");
	}
}
