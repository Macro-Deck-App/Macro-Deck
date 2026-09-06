using MacroDeck.Plugin.Analyzers.Rules;
using MacroDeck.Plugin.Analyzers.Tests.UnitTests.Support;

namespace MacroDeck.Plugin.Analyzers.Tests.UnitTests;

/// <summary>
/// MDP3001 fires inside two different methods - ICapabilityHandler.InvokeAsync (token is a direct
/// parameter) and IActionExecutor.ExecuteAsync (token is context.CancellationToken) - each with its own
/// in-scope token expression, so both get their own positive/near-miss pair.
/// </summary>
[TestFixture]
public class MissingCancellationForwardingAnalyzerTests
{
	[Test]
	public async Task Fires_when_InvokeAsync_passes_CancellationToken_None_instead_of_its_own_token()
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

							  	public async Task<CapabilityInvocationResult> InvokeAsync(CapabilityInvocation invocation, CancellationToken cancellationToken)
							  	{
							  		await Task.Delay(1000, CancellationToken.None);
							  		return CapabilityInvocationResult.Ok();
							  	}
							  }
							  """;

		var diagnostics
			= await AnalyzerTestHarness.GetDiagnosticsAsync(source, new MissingCancellationForwardingAnalyzer());

		Assert.That(diagnostics, Has.One.Items);
		Assert.That(diagnostics[0].Id, Is.EqualTo("MDP3001"));
		Assert.That(AnalyzerTestHarness.GetSquiggleText(diagnostics[0], source), Is.EqualTo("CancellationToken.None"));
	}

	[Test]
	public async Task Does_not_fire_when_InvokeAsync_forwards_its_own_token()
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

							  	public async Task<CapabilityInvocationResult> InvokeAsync(CapabilityInvocation invocation, CancellationToken cancellationToken)
							  	{
							  		await Task.Delay(1000, cancellationToken);
							  		return CapabilityInvocationResult.Ok();
							  	}
							  }
							  """;

		var diagnostics
			= await AnalyzerTestHarness.GetDiagnosticsAsync(source, new MissingCancellationForwardingAnalyzer());

		Assert.That(diagnostics, Is.Empty);
	}

	/// <summary>
	/// Task.Delay is not usable for this: Task.Delay(int) and Task.Delay(int, CancellationToken) are two
	/// separate overloads, not one method with an optional token, so calling the first is not "omitting"
	/// anything - there is nothing to omit. A method that actually declares an optional token parameter
	/// is needed to exercise that half of the rule.
	/// </summary>
	[Test]
	public async Task Fires_when_ExecuteAsync_omits_the_optional_token_available_from_its_context()
	{
		const string source = """
							  using System.Threading;
							  using System.Threading.Tasks;
							  using MacroDeck.Sdk.Actions;

							  internal sealed class MyExecutor : IActionExecutor
							  {
							  	public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
							  	{
							  		await DoWorkAsync();
							  		return ActionResult.Success();
							  	}

							  	private static Task DoWorkAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
							  }
							  """;

		var diagnostics
			= await AnalyzerTestHarness.GetDiagnosticsAsync(source, new MissingCancellationForwardingAnalyzer());

		Assert.That(diagnostics, Has.One.Items);
		Assert.That(diagnostics[0].Id, Is.EqualTo("MDP3001"));
		Assert.That(AnalyzerTestHarness.GetSquiggleText(diagnostics[0], source), Is.EqualTo("DoWorkAsync()"));
	}

	/// <summary>
	/// The exact shape MacroDeck.Plugin.Testing.Tests.MisbehavingPlugin's IgnoreCancellation behaviour
	/// uses to simulate an executor that ignores cancellation - explicit CancellationToken.None passed to
	/// an argument that has nothing to do with the omitted-optional-parameter case above.
	/// </summary>
	[Test]
	public async Task Fires_when_ExecuteAsync_passes_CancellationToken_None_explicitly()
	{
		const string source = """
							  using System.Threading;
							  using System.Threading.Tasks;
							  using MacroDeck.Sdk.Actions;

							  internal sealed class MyExecutor : IActionExecutor
							  {
							  	public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
							  	{
							  		await Task.Delay(Timeout.Infinite, CancellationToken.None);
							  		return ActionResult.Success();
							  	}
							  }
							  """;

		var diagnostics
			= await AnalyzerTestHarness.GetDiagnosticsAsync(source, new MissingCancellationForwardingAnalyzer());

		Assert.That(diagnostics, Has.One.Items);
		Assert.That(diagnostics[0].Id, Is.EqualTo("MDP3001"));
		Assert.That(AnalyzerTestHarness.GetSquiggleText(diagnostics[0], source), Is.EqualTo("CancellationToken.None"));
	}

	[Test]
	public async Task Does_not_fire_when_ExecuteAsync_forwards_context_CancellationToken()
	{
		const string source = """
							  using System.Threading.Tasks;
							  using MacroDeck.Sdk.Actions;

							  internal sealed class MyExecutor : IActionExecutor
							  {
							  	public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
							  	{
							  		await Task.Delay(1000, context.CancellationToken);
							  		return ActionResult.Success();
							  	}
							  }
							  """;

		var diagnostics
			= await AnalyzerTestHarness.GetDiagnosticsAsync(source, new MissingCancellationForwardingAnalyzer());

		Assert.That(diagnostics, Is.Empty);
	}
}
