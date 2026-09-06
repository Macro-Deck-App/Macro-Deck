using MacroDeck.Plugin.Analyzers.Rules;
using MacroDeck.Plugin.Analyzers.Tests.UnitTests.Support;

namespace MacroDeck.Plugin.Analyzers.Tests.UnitTests;

[TestFixture]
public class InertIntegrationAttributeAnalyzerTests
{
	[Test]
	public async Task Fires_when_MacroDeckIntegration_is_applied_to_an_IPluginIntegration_type()
	{
		const string source = """
							  using System.Collections.Generic;
							  using System.Threading.Tasks;
							  using MacroDeck.Localization;
							  using MacroDeck.Sdk;
							  using MacroDeck.Sdk.Actions;

							  [MacroDeckIntegration]
							  internal sealed class MyIntegration : IPluginIntegration
							  {
							  	public IReadOnlyList<IActionDefinition> Actions { get; } = [];
							  	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;
							  	public Task ShutdownAsync() => Task.CompletedTask;
							  }
							  """;

		var diagnostics
			= await AnalyzerTestHarness.GetDiagnosticsAsync(source, new InertIntegrationAttributeAnalyzer());

		Assert.That(diagnostics, Has.One.Items);
		Assert.That(diagnostics[0].Id, Is.EqualTo("MDP2006"));
		Assert.That(diagnostics[0].Severity, Is.EqualTo(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning));
	}

	[Test]
	public async Task Does_not_fire_when_MacroDeckIntegration_is_applied_to_an_IIntegration_type()
	{
		// This is exactly how the in-process host discovers an IIntegration - the attribute is not inert
		// there, so it must never be flagged.
		const string source = """
							  using System.Collections.Generic;
							  using System.Threading.Tasks;
							  using MacroDeck.Localization;
							  using MacroDeck.Sdk;
							  using MacroDeck.Sdk.Actions;

							  [MacroDeckIntegration]
							  internal sealed class MyIntegration : IIntegration
							  {
							  	public string Id => "app.macro-deck.my-plugin.my-integration";
							  	public LocalizedText Name => "My Integration";
							  	public string Version => "1.0.0";
							  	public bool IsInitialized => false;
							  	public IReadOnlyList<IActionDefinition> Actions { get; } = [];
							  	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;
							  	public Task ShutdownAsync() => Task.CompletedTask;
							  }
							  """;

		var diagnostics
			= await AnalyzerTestHarness.GetDiagnosticsAsync(source, new InertIntegrationAttributeAnalyzer());

		Assert.That(diagnostics, Is.Empty);
	}

	[Test]
	public async Task Does_not_fire_on_an_IPluginIntegration_type_with_no_attribute()
	{
		const string source = """
							  using System.Collections.Generic;
							  using System.Threading.Tasks;
							  using MacroDeck.Localization;
							  using MacroDeck.Sdk;
							  using MacroDeck.Sdk.Actions;

							  internal sealed class MyIntegration : IPluginIntegration
							  {
							  	public IReadOnlyList<IActionDefinition> Actions { get; } = [];
							  	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;
							  	public Task ShutdownAsync() => Task.CompletedTask;
							  }
							  """;

		var diagnostics
			= await AnalyzerTestHarness.GetDiagnosticsAsync(source, new InertIntegrationAttributeAnalyzer());

		Assert.That(diagnostics, Is.Empty);
	}
}
