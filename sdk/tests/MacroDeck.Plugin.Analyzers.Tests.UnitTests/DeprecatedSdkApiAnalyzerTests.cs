using MacroDeck.Plugin.Analyzers.Rules;
using MacroDeck.Plugin.Analyzers.Tests.UnitTests.Support;
using Microsoft.CodeAnalysis;

namespace MacroDeck.Plugin.Analyzers.Tests.UnitTests;

/// <summary>
/// MDP5002 (still-live deprecation) and MDP5004 (past its declared removal version) - issue #418's
/// requirement that a deprecated SDK API produces a stable diagnostic naming the affected API, the
/// deprecation version, the planned removal version, and actionable migration guidance.
///
/// <para>
/// [MacroDeckDeprecated] is not restricted to a tracked assembly name the way MDP5001 is - the attribute
/// itself is the opt-in - so these snippets declare the deprecated member locally rather than passing
/// assemblyName, and control "has the SDK reached its removal version" with an explicit
/// [assembly: AssemblyVersion] rather than relying on whatever the test process happens to be compiled as.
/// </para>
/// </summary>
[TestFixture]
public class DeprecatedSdkApiAnalyzerTests
{
	private const string StillLiveSource = """
										   using System;
										   using System.Reflection;
										   using MacroDeck.Sdk.Deprecation;

										   [assembly: AssemblyVersion("1.0.0.0")]

										   internal static class LegacyHelpers
										   {
										   	[Obsolete("Use NewHelper instead.")]
										   	[MacroDeckDeprecated("3.1.0", "4.0.0", "Use NewHelper instead.", Replacement = "LegacyHelpers.NewHelper")]
										   	public static void OldHelper()
										   	{
										   	}

										   	public static void NewHelper()
										   	{
										   	}
										   }

										   internal static class Usage
										   {
										   	public static void CallOld() => LegacyHelpers.OldHelper();

										   	public static void CallNew() => LegacyHelpers.NewHelper();
										   }
										   """;

	[Test]
	public async Task
		MDP5002_fires_at_a_call_site_naming_the_API_the_deprecation_and_removal_versions_and_the_guidance()
	{
		var diagnostics
			= await AnalyzerTestHarness.GetDiagnosticsAsync(StillLiveSource, new DeprecatedSdkApiAnalyzer());

		Assert.That(diagnostics, Has.One.Items);
		var diagnostic = diagnostics[0];

		Assert.That(diagnostic.Id, Is.EqualTo("MDP5002"));
		Assert.That(AnalyzerTestHarness.GetSquiggleText(diagnostic, StillLiveSource),
			Is.EqualTo("LegacyHelpers.OldHelper()"));

		var message = diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
		Assert.Multiple(() =>
		{
			Assert.That(message, Does.Contain("OldHelper"), "names the affected API");
			Assert.That(message, Does.Contain("3.1.0"), "states the deprecation version");
			Assert.That(message, Does.Contain("4.0.0"), "states the planned removal version");
			Assert.That(message, Does.Contain("LegacyHelpers.NewHelper"), "carries actionable migration guidance");
		});
	}

	/// <summary>
	/// Counterexample for the requirement above: an implementation that flagged every SDK call, rather
	/// than only ones carrying [MacroDeckDeprecated], would still pass a positive-only test. NewHelper is
	/// declared side by side with OldHelper, in the same class, with no deprecation attribute of its own.
	/// </summary>
	[Test]
	public async Task MDP5002_does_not_fire_on_an_equivalent_API_that_carries_no_deprecation_attribute()
	{
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync("""
																		using System;
																		using MacroDeck.Sdk.Deprecation;

																		internal static class LegacyHelpers
																		{
																			public static void NewHelper()
																			{
																			}
																		}

																		internal static class Usage
																		{
																			public static void Call() => LegacyHelpers.NewHelper();
																		}
																		""",
			new DeprecatedSdkApiAnalyzer());

		Assert.That(diagnostics, Is.Empty);
	}

	/// <summary>
	/// The same declaration as <see cref="StillLiveSource" />, except the assembly's own version has
	/// already reached RemovedIn ("4.0.0") - MDP5004 fires instead of MDP5002, as an Error rather than a
	/// Warning, because at that point the removal promise has already been broken.
	/// </summary>
	[Test]
	public async Task MDP5004_fires_instead_of_MDP5002_once_the_declaring_assembly_has_reached_RemovedIn()
	{
		const string source = """
							  using System;
							  using System.Reflection;
							  using MacroDeck.Sdk.Deprecation;

							  [assembly: AssemblyVersion("4.2.0.0")]

							  internal static class LegacyHelpers
							  {
							  	[Obsolete("Use NewHelper instead.")]
							  	[MacroDeckDeprecated("3.1.0", "4.0.0", "Use NewHelper instead.", Replacement = "LegacyHelpers.NewHelper")]
							  	public static void OldHelper()
							  	{
							  	}
							  }

							  internal static class Usage
							  {
							  	public static void Call() => LegacyHelpers.OldHelper();
							  }
							  """;

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, new DeprecatedSdkApiAnalyzer());

		Assert.That(diagnostics, Has.One.Items);
		var diagnostic = diagnostics[0];

		Assert.Multiple(() =>
		{
			Assert.That(diagnostic.Id, Is.EqualTo("MDP5004"));
			Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Error));
		});

		var message = diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
		Assert.Multiple(() =>
		{
			Assert.That(message, Does.Contain("OldHelper"), "names the affected API");
			Assert.That(message, Does.Contain("4.0.0"), "states the declared removal version");
			Assert.That(message, Does.Contain("4.2.0.0"), "states the SDK version that has already reached it");
		});
	}
}
