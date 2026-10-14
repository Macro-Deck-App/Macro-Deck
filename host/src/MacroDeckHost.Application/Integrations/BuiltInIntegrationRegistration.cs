namespace MacroDeckHost.Application.Integrations;

public sealed class BuiltInIntegrationRegistration
{
	private readonly TaskCompletionSource _completed = new();

	public bool IsComplete => _completed.Task.IsCompleted;

	public Task Completed => _completed.Task;

	public void Complete()
	{
		_completed.TrySetResult();
	}
}
