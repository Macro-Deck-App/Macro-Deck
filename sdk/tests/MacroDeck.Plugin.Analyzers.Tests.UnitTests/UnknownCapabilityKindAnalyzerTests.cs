using MacroDeck.Plugin.Analyzers.Rules;
using MacroDeck.Plugin.Analyzers.Tests.UnitTests.Support;

namespace MacroDeck.Plugin.Analyzers.Tests.UnitTests;

[TestFixture]
public class UnknownCapabilityKindAnalyzerTests
{
	[Test]
	public async Task Fires_when_Kind_returns_a_constant_outside_the_known_kinds()
	{
		const string source = """
							  using System.Collections.Generic;
							  using System.Threading;
							  using System.Threading.Tasks;
							  using MacroDeck.Plugin.Hosting.Capabilities;
							  using MacroDeck.Plugin.Protocol.Handshake;

							  internal sealed class MyHandler : ICapabilityHandler
							  {
							  	public string Kind => "not-a-kind";
							  	public IReadOnlyList<DeclaredCapability> DeclareCapabilities() => [];
							  	public Task<CapabilityInvocationResult> InvokeAsync(CapabilityInvocation invocation, CancellationToken cancellationToken)
							  		=> throw new System.NotImplementedException();
							  }
							  """;

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, new UnknownCapabilityKindAnalyzer());

		Assert.That(diagnostics, Has.One.Items);
		Assert.That(diagnostics[0].Id, Is.EqualTo("MDP2002"));
		Assert.That(AnalyzerTestHarness.GetSquiggleText(diagnostics[0], source), Is.EqualTo("\"not-a-kind\""));
	}

	[Test]
	public async Task Does_not_fire_when_Kind_returns_a_known_kind()
	{
		const string source = """
							  using System.Collections.Generic;
							  using System.Threading;
							  using System.Threading.Tasks;
							  using MacroDeck.Plugin.Hosting.Capabilities;
							  using MacroDeck.Plugin.Protocol.Handshake;

							  internal sealed class MyHandler : ICapabilityHandler
							  {
							  	public string Kind => CapabilityKinds.Events;
							  	public IReadOnlyList<DeclaredCapability> DeclareCapabilities() => [];
							  	public Task<CapabilityInvocationResult> InvokeAsync(CapabilityInvocation invocation, CancellationToken cancellationToken)
							  		=> throw new System.NotImplementedException();
							  }
							  """;

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, new UnknownCapabilityKindAnalyzer());

		Assert.That(diagnostics, Is.Empty);
	}
}
