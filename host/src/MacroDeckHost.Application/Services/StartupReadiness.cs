namespace MacroDeckHost.Application.Services;

public sealed class StartupReadiness
{
	private readonly TaskCompletionSource _caches = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly TaskCompletionSource _variables = new(TaskCreationOptions.RunContinuationsAsynchronously);

	public Task WhenReady => Task.WhenAll(_caches.Task, _variables.Task);

	public bool IsReady => _caches.Task.IsCompletedSuccessfully && _variables.Task.IsCompletedSuccessfully;

	public void MarkCachesReady() => _caches.TrySetResult();

	public void MarkVariablesReady() => _variables.TrySetResult();
}
