using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Rendering;

// An onStateChange flow a press runs inline has no deadline from the press itself (#1552), so a flow that
// never ends must at least end with the host instead of keeping shutdown waiting.
public class WidgetStateChangeFlowShutdownTests
{
	[Test]
	public async Task An_endless_inline_on_state_change_flow_ends_when_the_host_stops()
	{
		using var lifetime = new StoppableLifetime();
		var runner = new WidgetStateChangeFlowRunner(new ServiceCollection().BuildServiceProvider()
				.GetRequiredService<IServiceScopeFactory>(),
			new WidgetStateEvalChannel(),
			lifetime,
			Log.Logger);
		var executor = new EndlessFlowExecutor();

		var run = runner.RunInline(Guid.NewGuid(), executor, StateChangeRequest(), CancellationToken.None);
		await executor.Started;
		lifetime.StopApplication();

		await run.WaitAsync(TimeSpan.FromSeconds(10));
	}

	private static FlowExecutionRequest StateChangeRequest()
	{
		return new FlowExecutionRequest { Trigger = TriggerSelector.ByType("onStateChange") };
	}

	private sealed class EndlessFlowExecutor : IFlowExecutor
	{
		private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Task Started => _started.Task;

		public async Task<FlowExecutionResult> ExecuteAsync(
			FlowExecutionRequest request,
			CancellationToken cancellationToken)
		{
			_started.TrySetResult();
			await Task.Delay(Timeout.Infinite, cancellationToken);
			throw new InvalidOperationException("An endless flow never finishes on its own.");
		}
	}

	private sealed class StoppableLifetime : IHostApplicationLifetime, IDisposable
	{
		private readonly CancellationTokenSource _stopping = new();

		public CancellationToken ApplicationStarted => new(true);

		public CancellationToken ApplicationStopping => _stopping.Token;

		public CancellationToken ApplicationStopped => CancellationToken.None;

		public void StopApplication()
		{
			_stopping.Cancel();
		}

		public void Dispose()
		{
			_stopping.Dispose();
		}
	}
}
