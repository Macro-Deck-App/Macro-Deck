using MacroDeck.Plugin.Analyzers.Rules;
using MacroDeck.Plugin.Analyzers.Tests.UnitTests.Support;

namespace MacroDeck.Plugin.Analyzers.Tests.UnitTests;

[TestFixture]
public class UnroutedCapabilityHandlerAnalyzerTests
{
	// All usings for both variants live here, at the very top: a using directive can only precede type
	// declarations in a file, so the handler declaration and the per-test registration snippet below
	// cannot each bring their own.
	private const string HandlerDeclaration = """
											  using System.Collections.Generic;
											  using System.Threading;
											  using System.Threading.Tasks;
											  using MacroDeck.Plugin.Hosting;
											  using MacroDeck.Plugin.Hosting.Capabilities;
											  using MacroDeck.Plugin.Protocol.Handshake;
											  using Microsoft.Extensions.DependencyInjection;

											  internal sealed class MyHandler : ICapabilityHandler
											  {
											  	public string Kind => CapabilityKinds.Events;
											  	public IReadOnlyList<DeclaredCapability> DeclareCapabilities() => [];
											  	public Task<CapabilityInvocationResult> InvokeAsync(CapabilityInvocation invocation, CancellationToken cancellationToken)
											  		=> throw new System.NotImplementedException();
											  }

											  """;

	[Test]
	public async Task Fires_when_a_handler_is_registered_with_AddSingleton_alone()
	{
		var source = HandlerDeclaration +
			"""
			internal static class Registration
			{
				public static void Configure(IServiceCollection services)
				{
					services.AddSingleton<MyHandler>();
				}
			}
			""";

		var diagnostics
			= await AnalyzerTestHarness.GetDiagnosticsAsync(source, new UnroutedCapabilityHandlerAnalyzer());

		Assert.That(diagnostics, Has.One.Items);
		Assert.That(diagnostics[0].Id, Is.EqualTo("MDP2003"));
		Assert.That(AnalyzerTestHarness.GetSquiggleText(diagnostics[0], source),
			Is.EqualTo("services.AddSingleton<MyHandler>()"));
	}

	[Test]
	public async Task Does_not_fire_when_a_handler_is_registered_through_builder_RegisterCapabilityHandler()
	{
		// AddMacroDeckCapabilityHandler<T>() is internal to MacroDeck.Plugin.Hosting since #560, so a
		// plugin project - and this test snippet, compiled the same way one is - registers through the
		// public door, PluginHostBuilder.RegisterCapabilityHandler<T>(), instead.
		var source = HandlerDeclaration +
			"""
			internal static class Registration
			{
				public static void Configure(PluginHostBuilder builder)
				{
					builder.RegisterCapabilityHandler<MyHandler>();
				}
			}
			""";

		var diagnostics
			= await AnalyzerTestHarness.GetDiagnosticsAsync(source, new UnroutedCapabilityHandlerAnalyzer());

		Assert.That(diagnostics, Is.Empty);
	}
}
