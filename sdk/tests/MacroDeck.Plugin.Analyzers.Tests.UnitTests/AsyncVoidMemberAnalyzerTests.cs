using MacroDeck.Plugin.Analyzers.Rules;
using MacroDeck.Plugin.Analyzers.Tests.UnitTests.Support;

namespace MacroDeck.Plugin.Analyzers.Tests.UnitTests;

[TestFixture]
public class AsyncVoidMemberAnalyzerTests
{
	[Test]
	public async Task Fires_on_an_async_void_member_of_a_type_implementing_IIntegration()
	{
		const string source = """
							  using System.Collections.Generic;
							  using System.Threading.Tasks;
							  using MacroDeck.Localization;
							  using MacroDeck.Sdk;
							  using MacroDeck.Sdk.Actions;

							  internal sealed class MyIntegration : IIntegration
							  {
							  	public string Id => "app.macro-deck.my-plugin.my-integration";
							  	public LocalizedText Name => "My Integration";
							  	public string Version => "1.0.0";
							  	public IReadOnlyList<IActionDefinition> Actions { get; } = [];
							  	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;
							  	public Task ShutdownAsync() => Task.CompletedTask;
							  	public bool IsInitialized => false;

							  	private async void FireAndForget()
							  	{
							  		await Task.Delay(1);
							  	}
							  }
							  """;

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, new AsyncVoidMemberAnalyzer());

		Assert.That(diagnostics, Has.One.Items);
		Assert.That(diagnostics[0].Id, Is.EqualTo("MDP3003"));
		Assert.That(AnalyzerTestHarness.GetSquiggleText(diagnostics[0], source), Is.EqualTo("FireAndForget"));
	}

	[Test]
	public async Task Does_not_fire_on_an_async_Task_member_of_a_type_implementing_IIntegration()
	{
		const string source = """
							  using System.Collections.Generic;
							  using System.Threading.Tasks;
							  using MacroDeck.Localization;
							  using MacroDeck.Sdk;
							  using MacroDeck.Sdk.Actions;

							  internal sealed class MyIntegration : IIntegration
							  {
							  	public string Id => "app.macro-deck.my-plugin.my-integration";
							  	public LocalizedText Name => "My Integration";
							  	public string Version => "1.0.0";
							  	public IReadOnlyList<IActionDefinition> Actions { get; } = [];
							  	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;
							  	public Task ShutdownAsync() => Task.CompletedTask;
							  	public bool IsInitialized => false;

							  	private async Task BackgroundWorkAsync()
							  	{
							  		await Task.Delay(1);
							  	}
							  }
							  """;

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, new AsyncVoidMemberAnalyzer());

		Assert.That(diagnostics, Is.Empty);
	}
}
