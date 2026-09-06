using MacroDeck.Plugin.Analyzers.Rules;
using MacroDeck.Plugin.Analyzers.Tests.UnitTests.Support;

namespace MacroDeck.Plugin.Analyzers.Tests.UnitTests;

/// <summary>
/// MDP1002 fires on two independent shapes - an action's <c>Id</c> and a
/// <c>DeclaredCapability.LocalId</c> - each exercising a different code path in the analyzer, so both get
/// their own positive/near-miss pair rather than the usual single pair.
/// </summary>
[TestFixture]
public class DeclaredLocalIdGrammarAnalyzerTests
{
	[Test]
	public async Task Fires_when_an_action_Id_constant_is_not_kebab_case()
	{
		const string source = """
							  using MacroDeck.Localization;
							  using MacroDeck.Sdk.Actions;

							  internal sealed class BadAction : IActionDefinition
							  {
							  	public string Id => "bad_id";
							  	public LocalizedText Name => "Bad";
							  	public LocalizedText Description => "Bad";
							  	public System.Collections.Generic.IReadOnlyList<ActionParameter> Parameters { get; } = [];
							  	public IActionExecutor CreateExecutor() => throw new System.NotImplementedException();
							  }
							  """;

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, new DeclaredLocalIdGrammarAnalyzer());

		Assert.That(diagnostics, Has.One.Items);
		Assert.That(diagnostics[0].Id, Is.EqualTo("MDP1002"));
		Assert.That(AnalyzerTestHarness.GetSquiggleText(diagnostics[0], source), Is.EqualTo("\"bad_id\""));
	}

	[Test]
	public async Task Does_not_fire_when_an_action_Id_constant_is_kebab_case()
	{
		const string source = """
							  using MacroDeck.Localization;
							  using MacroDeck.Sdk.Actions;

							  internal sealed class GoodAction : IActionDefinition
							  {
							  	public string Id => "set-volume";
							  	public LocalizedText Name => "Set volume";
							  	public LocalizedText Description => "Sets the volume";
							  	public System.Collections.Generic.IReadOnlyList<ActionParameter> Parameters { get; } = [];
							  	public IActionExecutor CreateExecutor() => throw new System.NotImplementedException();
							  }
							  """;

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, new DeclaredLocalIdGrammarAnalyzer());

		Assert.That(diagnostics, Is.Empty);
	}

	[Test]
	public async Task Fires_when_a_DeclaredCapability_LocalId_constant_is_not_kebab_case()
	{
		const string source = """
							  using MacroDeck.Plugin.Protocol.Handshake;
							  using MacroDeck.Plugin.Protocol.Versioning;

							  internal static class Factory
							  {
							  	public static DeclaredCapability Create() => new DeclaredCapability
							  	{
							  		Kind = CapabilityKinds.Events,
							  		LocalId = "Not Valid",
							  		VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
							  	};
							  }
							  """;

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, new DeclaredLocalIdGrammarAnalyzer());

		Assert.That(diagnostics, Has.One.Items);
		Assert.That(diagnostics[0].Id, Is.EqualTo("MDP1002"));
		Assert.That(AnalyzerTestHarness.GetSquiggleText(diagnostics[0], source), Is.EqualTo("\"Not Valid\""));
	}

	[Test]
	public async Task Does_not_fire_when_a_DeclaredCapability_LocalId_constant_is_kebab_case()
	{
		const string source = """
							  using MacroDeck.Plugin.Protocol.Handshake;
							  using MacroDeck.Plugin.Protocol.Versioning;

							  internal static class Factory
							  {
							  	public static DeclaredCapability Create() => new DeclaredCapability
							  	{
							  		Kind = CapabilityKinds.Events,
							  		LocalId = "volume-changed",
							  		VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
							  	};
							  }
							  """;

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, new DeclaredLocalIdGrammarAnalyzer());

		Assert.That(diagnostics, Is.Empty);
	}
}
