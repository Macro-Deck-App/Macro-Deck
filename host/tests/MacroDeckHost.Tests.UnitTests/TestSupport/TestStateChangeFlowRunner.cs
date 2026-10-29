using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

public static class TestStateChangeFlowRunner
{
	public static WidgetStateChangeFlowRunner Create(
		IFlowExecutor? detachedFlowExecutor = null,
		WidgetStateEvalChannel? queue = null,
		ILogger? logger = null)
	{
		var services = new ServiceCollection();
		if (detachedFlowExecutor is not null)
		{
			services.AddScoped(_ => detachedFlowExecutor);
		}

		return new WidgetStateChangeFlowRunner(
			services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
			queue ?? new WidgetStateEvalChannel(),
			new RunningHostLifetime(),
			logger ?? Serilog.Log.Logger);
	}
}

public sealed class RunningHostLifetime : IHostApplicationLifetime
{
	public CancellationToken ApplicationStarted { get; } = new(true);
	public CancellationToken ApplicationStopping => CancellationToken.None;
	public CancellationToken ApplicationStopped => CancellationToken.None;

	public void StopApplication()
	{
	}
}
