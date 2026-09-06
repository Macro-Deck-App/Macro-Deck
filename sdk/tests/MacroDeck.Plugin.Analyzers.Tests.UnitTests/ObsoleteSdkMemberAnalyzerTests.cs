using MacroDeck.Plugin.Analyzers.Rules;
using MacroDeck.Plugin.Analyzers.Tests.UnitTests.Support;

namespace MacroDeck.Plugin.Analyzers.Tests.UnitTests;

/// <summary>
/// The SDK ships zero [Obsolete] members today (#416 ships the mechanism, #418 owns the policy), so this
/// is tested against a synthetic [Obsolete] member declared in the test compilation itself - which the
/// harness's assemblyName parameter lets the positive test name after the real SDK assembly, so the
/// member and its usage look, to the analyzer, exactly like they came from it.
/// </summary>
[TestFixture]
public class ObsoleteSdkMemberAnalyzerTests
{
	private const string Source = """
								  using System;

								  internal static class LegacyHelpers
								  {
								  	[Obsolete("Use NewHelper instead.")]
								  	public static void OldHelper()
								  	{
								  	}
								  }

								  internal static class Usage
								  {
								  	public static void Call() => LegacyHelpers.OldHelper();
								  }
								  """;

	[Test]
	public async Task Fires_when_the_obsolete_member_belongs_to_a_tracked_SDK_assembly()
	{
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(Source,
			new ObsoleteSdkMemberAnalyzer(),
			assemblyName: AnalyzerTestHarness.SdkAssemblyName);

		Assert.That(diagnostics, Has.One.Items);
		Assert.That(diagnostics[0].Id, Is.EqualTo("MDP5001"));
		Assert.That(AnalyzerTestHarness.GetSquiggleText(diagnostics[0], Source),
			Is.EqualTo("LegacyHelpers.OldHelper()"));
	}

	/// <summary>
	/// The exact same obsolete member and usage, but compiled as an unrelated assembly - the compiler's
	/// own CS0618 still fires here (this is not what MDP5001 replaces), but MDP5001 must not, because
	/// MDP5001 exists specifically to be gateable independently of every other obsolete-API warning in a
	/// plugin author's own code.
	/// </summary>
	[Test]
	public async Task Does_not_fire_when_the_obsolete_member_belongs_to_an_untracked_assembly()
	{
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(Source, new ObsoleteSdkMemberAnalyzer());

		Assert.That(diagnostics, Is.Empty);
	}

	/// <summary>
	/// A member that carries both [Obsolete] and [MacroDeckDeprecated] is MDP5002/MDP5004's territory,
	/// which reports the richer, actionable message - MDP5001 stands down so the same call site is not
	/// reported twice under two different ids.
	/// </summary>
	[Test]
	public async Task Stands_down_when_the_obsolete_member_also_carries_MacroDeckDeprecated()
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

							  internal static class Usage
							  {
							  	public static void Call() => LegacyHelpers.OldHelper();
							  }
							  """;

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source,
			new ObsoleteSdkMemberAnalyzer(),
			assemblyName: AnalyzerTestHarness.SdkAssemblyName);

		Assert.That(diagnostics, Is.Empty);
	}
}
