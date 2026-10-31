using MacroDeckHost.Application.Actions;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal sealed class InlineActionExecutionCoordinator : IActionExecutionCoordinator
{
	private readonly IServiceScopeFactory _scopes;

	public int Runs { get; private set; }

	public InlineActionExecutionCoordinator(IServiceScopeFactory scopes)
	{
		_scopes = scopes;
	}

	public async Task<ActionExecutionDispatch> RunBoundedAsync(
		FlowExecutionRequest request,
		TimeSpan bound,
		CancellationToken cancellationToken)
	{
		Runs++;
		await using var scope = _scopes.CreateAsyncScope();
		var result = await scope.ServiceProvider.GetRequiredService<IFlowExecutor>()
			.ExecuteAsync(request, cancellationToken);

		return new ActionExecutionDispatch(request.ExecutionId, result);
	}

	public Task<ActionExecutionDispatch> RunBoundedAsync(
		Func<IServiceProvider, CancellationToken, Task<FlowExecutionResult>> run,
		TimeSpan bound,
		CancellationToken cancellationToken)
	{
		throw new NotSupportedException();
	}
}
