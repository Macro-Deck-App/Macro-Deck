using MacroDeck.Plugin.Analyzers.Rules;
using MacroDeck.Plugin.Analyzers.Tests.UnitTests.Support;

namespace MacroDeck.Plugin.Analyzers.Tests.UnitTests;

[TestFixture]
public class RawIntegrationRegistrationAnalyzerTests
{
	// All usings for both variants live here, at the very top: a using directive can only precede type
	// declarations in a file, so the integration declaration and the per-test registration snippet below
	// cannot each bring their own.
	private const string IntegrationDeclaration = """
												  using System.Collections.Generic;
												  using System.Threading.Tasks;
												  using MacroDeck.Sdk;
												  using MacroDeck.Sdk.Actions;
												  using MacroDeck.Plugin.Hosting;
												  using Microsoft.Extensions.DependencyInjection;

												  internal sealed class MyIntegration : IPluginIntegration
												  {
												  	public IReadOnlyList<IActionDefinition> Actions { get; } = [];
												  	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;
												  	public Task ShutdownAsync() => Task.CompletedTask;
												  }

												  """;

	[Test]
	public async Task Fires_when_an_integration_is_registered_with_raw_AddSingleton()
	{
		var source = IntegrationDeclaration +
			"""
			internal static class Registration
			{
				public static void Configure(IServiceCollection services)
				{
					services.AddSingleton<MyIntegration>();
				}
			}
			""";

		var diagnostics
			= await AnalyzerTestHarness.GetDiagnosticsAsync(source, new RawIntegrationRegistrationAnalyzer());

		Assert.That(diagnostics, Has.One.Items);
		Assert.That(diagnostics[0].Id, Is.EqualTo("MDP2004"));
		Assert.That(AnalyzerTestHarness.GetSquiggleText(diagnostics[0], source),
			Is.EqualTo("services.AddSingleton<MyIntegration>()"));
	}

	[Test]
	public async Task Does_not_fire_when_an_integration_is_registered_through_builder_RegisterIntegration()
	{
		// AddMacroDeckIntegration<T>() is internal to MacroDeck.Plugin.Hosting since #560, so a plugin
		// project - and this test snippet, compiled the same way one is - registers through the public
		// door, PluginHostBuilder.RegisterIntegration<T>(), instead.
		var source = IntegrationDeclaration +
			"""
			internal static class Registration
			{
				public static void Configure(PluginHostBuilder builder)
				{
					builder.RegisterIntegration<MyIntegration>();
				}
			}
			""";

		var diagnostics
			= await AnalyzerTestHarness.GetDiagnosticsAsync(source, new RawIntegrationRegistrationAnalyzer());

		Assert.That(diagnostics, Is.Empty);
	}

	[Test]
	public async Task Does_not_fire_on_AddSingleton_IPluginIntegration_with_a_factory()
	{
		// This is the exact shape RegisterIntegration<T>() uses internally
		// (services.AddSingleton<IPluginIntegration>(factory)) - one type argument that *is*
		// IPluginIntegration, not a type that implements it. The rule must not flag the SDK's own
		// correct registration path.
		var source = IntegrationDeclaration +
			"""
			internal static class Registration
			{
				public static void Configure(IServiceCollection services)
				{
					services.AddSingleton<IPluginIntegration>(provider => new MyIntegration());
				}
			}
			""";

		var diagnostics
			= await AnalyzerTestHarness.GetDiagnosticsAsync(source, new RawIntegrationRegistrationAnalyzer());

		Assert.That(diagnostics, Is.Empty);
	}
}
