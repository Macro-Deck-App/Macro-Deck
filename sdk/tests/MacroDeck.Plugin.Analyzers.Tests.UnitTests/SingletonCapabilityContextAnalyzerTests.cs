using MacroDeck.Plugin.Analyzers.Rules;
using MacroDeck.Plugin.Analyzers.Tests.UnitTests.Support;

namespace MacroDeck.Plugin.Analyzers.Tests.UnitTests;

[TestFixture]
public class SingletonCapabilityContextAnalyzerTests
{
	private const string ServiceDeclaration = """
											  using MacroDeck.Plugin.Hosting.Capabilities;
											  using Microsoft.Extensions.DependencyInjection;

											  internal sealed class MyService
											  {
											  	public MyService(ICapabilityInvocationContext context)
											  	{
											  	}
											  }

											  """;

	[Test]
	public async Task Fires_when_a_type_taking_ICapabilityInvocationContext_is_registered_as_a_singleton()
	{
		var source = ServiceDeclaration +
			"""
			internal static class Registration
			{
				public static void Configure(IServiceCollection services)
				{
					services.AddSingleton<MyService>();
				}
			}
			""";

		var diagnostics
			= await AnalyzerTestHarness.GetDiagnosticsAsync(source, new SingletonCapabilityContextAnalyzer());

		Assert.That(diagnostics, Has.One.Items);
		Assert.That(diagnostics[0].Id, Is.EqualTo("MDP4001"));
		// A parameter symbol's own location is just its identifier, not its type - "context", not
		// "ICapabilityInvocationContext context".
		Assert.That(AnalyzerTestHarness.GetSquiggleText(diagnostics[0], source), Is.EqualTo("context"));
	}

	[Test]
	public async Task Does_not_fire_when_the_same_type_is_registered_scoped_instead_of_singleton()
	{
		var source = ServiceDeclaration +
			"""
			internal static class Registration
			{
				public static void Configure(IServiceCollection services)
				{
					services.AddScoped<MyService>();
				}
			}
			""";

		var diagnostics
			= await AnalyzerTestHarness.GetDiagnosticsAsync(source, new SingletonCapabilityContextAnalyzer());

		Assert.That(diagnostics, Is.Empty);
	}

	[Test]
	public async Task Fires_when_registered_through_builder_RegisterIntegration()
	{
		// RegisterIntegration<T>() registers T as a singleton internally (through the now-internal
		// AddMacroDeckIntegration<T>()), exactly like a direct AddSingleton<T>() does - this is the third
		// arm MDP4001 gained alongside AddMacroDeckIntegration/AddMacroDeckCapabilityHandler when
		// PluginHostBuilder became the author-facing door onto both.
		var source =
			"""
			using System.Collections.Generic;
			using System.Threading.Tasks;
			using MacroDeck.Sdk;
			using MacroDeck.Sdk.Actions;
			using MacroDeck.Plugin.Hosting;
			using MacroDeck.Plugin.Hosting.Capabilities;

			internal sealed class MyIntegration : IPluginIntegration
			{
				public MyIntegration(ICapabilityInvocationContext context)
				{
				}

				public IReadOnlyList<IActionDefinition> Actions { get; } = [];
				public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;
				public Task ShutdownAsync() => Task.CompletedTask;
			}

			internal static class Registration
			{
				public static void Configure(PluginHostBuilder builder)
				{
					builder.RegisterIntegration<MyIntegration>();
				}
			}
			""";

		var diagnostics
			= await AnalyzerTestHarness.GetDiagnosticsAsync(source, new SingletonCapabilityContextAnalyzer());

		Assert.That(diagnostics, Has.One.Items);
		Assert.That(diagnostics[0].Id, Is.EqualTo("MDP4001"));
	}
}
