using MacroDeck.Plugin.Analyzers.Rules;
using MacroDeck.Plugin.Analyzers.Tests.UnitTests.Support;

namespace MacroDeck.Plugin.Analyzers.Tests.UnitTests;

[TestFixture]
public class BlockingCallAnalyzerTests
{
	[Test]
	public async Task Fires_on_Task_Result_inside_a_capability_handler()
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
							  	{
							  		var value = Task.FromResult(1).Result;
							  		return Task.FromResult(CapabilityInvocationResult.Ok(value));
							  	}
							  }
							  """;

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, new BlockingCallAnalyzer());

		Assert.That(diagnostics, Has.One.Items);
		Assert.That(diagnostics[0].Id, Is.EqualTo("MDP3002"));
		Assert.That(AnalyzerTestHarness.GetSquiggleText(diagnostics[0], source),
			Is.EqualTo("Task.FromResult(1).Result"));
	}

	[Test]
	public async Task Does_not_fire_when_the_task_is_properly_awaited()
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
							  		var value = await Task.FromResult(1);
							  		return CapabilityInvocationResult.Ok(value);
							  	}
							  }
							  """;

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, new BlockingCallAnalyzer());

		Assert.That(diagnostics, Is.Empty);
	}

	[Test]
	public async Task Fires_on_Task_Wait_inside_an_action_executor()
	{
		const string source = """
							  using System.Threading.Tasks;
							  using MacroDeck.Sdk.Actions;

							  internal sealed class MyExecutor : IActionExecutor
							  {
							  	public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
							  	{
							  		Task.Delay(1).Wait();
							  		return Task.FromResult(ActionResult.Success());
							  	}
							  }
							  """;

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, new BlockingCallAnalyzer());

		Assert.That(diagnostics, Has.One.Items);
		Assert.That(diagnostics[0].Id, Is.EqualTo("MDP3002"));
	}

	[Test]
	public async Task Fires_on_GetAwaiter_GetResult_inside_a_config_flow()
	{
		const string source = """
							  using System.Collections.Generic;
							  using System.Threading;
							  using System.Threading.Tasks;
							  using MacroDeck.Sdk.ConfigFlow;

							  internal sealed class MyConfigFlow : IConfigFlow
							  {
							  	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
							  	{
							  		Task.Delay(1).GetAwaiter().GetResult();
							  		throw new System.NotImplementedException();
							  	}

							  	public Task<ConfigFlowResult> SubmitAsync(string stepId, IReadOnlyDictionary<string, object?> input, IConfigFlowContext context, CancellationToken cancellationToken)
							  		=> throw new System.NotImplementedException();
							  }
							  """;

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, new BlockingCallAnalyzer());

		Assert.That(diagnostics, Has.One.Items);
		Assert.That(diagnostics[0].Id, Is.EqualTo("MDP3002"));
	}

	[Test]
	public async Task Fires_on_Thread_Sleep_inside_a_capability_handler()
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
							  	{
							  		Thread.Sleep(10);
							  		return Task.FromResult(CapabilityInvocationResult.Ok());
							  	}
							  }
							  """;

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, new BlockingCallAnalyzer());

		Assert.That(diagnostics, Has.One.Items);
		Assert.That(diagnostics[0].Id, Is.EqualTo("MDP3002"));
	}
}
