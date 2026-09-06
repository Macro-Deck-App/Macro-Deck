using MacroDeck.Plugin.Analyzers.Rules;
using MacroDeck.Plugin.Analyzers.Tests.UnitTests.Support;

namespace MacroDeck.Plugin.Analyzers.Tests.UnitTests;

/// <summary>
/// MDP5003: a [MacroDeckDeprecated] declaration that a plugin author or the host cannot act on -
/// missing a companion [Obsolete], a removal version that is not after the deprecation version, or empty
/// guidance. This is the compile-time half of "actionable migration guidance" from issue #418: it fires
/// where a deprecation is declared, catching the case before it ever reaches a call site.
/// </summary>
[TestFixture]
public class DeprecationMetadataAnalyzerTests
{
	[Test]
	public async Task Fires_when_the_declaration_has_no_companion_Obsolete_attribute()
	{
		const string source = """
							  using MacroDeck.Sdk.Deprecation;

							  internal static class LegacyHelpers
							  {
							  	[MacroDeckDeprecated("3.1.0", "4.0.0", "Use NewHelper instead.")]
							  	public static void OldHelper()
							  	{
							  	}
							  }
							  """;

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, new DeprecationMetadataAnalyzer());

		Assert.That(diagnostics, Has.One.Items);
		Assert.That(diagnostics[0].Id, Is.EqualTo("MDP5003"));
		Assert.That(diagnostics[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture),
			Does.Contain("[Obsolete]"));
	}

	[Test]
	public async Task Fires_when_RemovedIn_is_not_after_DeprecatedIn()
	{
		const string source = """
							  using System;
							  using MacroDeck.Sdk.Deprecation;

							  internal static class LegacyHelpers
							  {
							  	[Obsolete("Use NewHelper instead.")]
							  	[MacroDeckDeprecated("3.1.0", "3.1.0", "Use NewHelper instead.")]
							  	public static void OldHelper()
							  	{
							  	}
							  }
							  """;

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, new DeprecationMetadataAnalyzer());

		Assert.That(diagnostics, Has.One.Items);
		Assert.That(diagnostics[0].Id, Is.EqualTo("MDP5003"));
		Assert.That(diagnostics[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture),
			Does.Contain("not after"));
	}

	[Test]
	public async Task Fires_when_guidance_is_empty()
	{
		const string source = """
							  using System;
							  using MacroDeck.Sdk.Deprecation;

							  internal static class LegacyHelpers
							  {
							  	[Obsolete("See docs.")]
							  	[MacroDeckDeprecated("3.1.0", "4.0.0", "")]
							  	public static void OldHelper()
							  	{
							  	}
							  }
							  """;

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, new DeprecationMetadataAnalyzer());

		Assert.That(diagnostics, Has.One.Items);
		Assert.That(diagnostics[0].Id, Is.EqualTo("MDP5003"));
		Assert.That(diagnostics[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture),
			Does.Contain("Guidance is empty"));
	}

	/// <summary>
	/// Counterexample for all three cases above: a declaration that has a companion [Obsolete], a
	/// removal version after its deprecation version, and non-empty guidance must not be reported. An
	/// implementation that reported every [MacroDeckDeprecated] declaration regardless of completeness
	/// would still pass the three positive tests above.
	/// </summary>
	[Test]
	public async Task Does_not_fire_on_a_fully_formed_declaration()
	{
		const string source = """
							  using System;
							  using MacroDeck.Sdk.Deprecation;

							  internal static class LegacyHelpers
							  {
							  	[Obsolete("Use NewHelper instead.")]
							  	[MacroDeckDeprecated("3.1.0", "4.0.0", "Use NewHelper instead.")]
							  	public static void OldHelper()
							  	{
							  	}
							  }
							  """;

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, new DeprecationMetadataAnalyzer());

		Assert.That(diagnostics, Is.Empty);
	}
}
