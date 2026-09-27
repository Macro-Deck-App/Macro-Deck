namespace MacroDeckHost.Application.Services;

public sealed class StartupReadiness
{
	private readonly TaskCompletionSource _caches = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly TaskCompletionSource _variables = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly TaskCompletionSource _iconPacks = new(TaskCreationOptions.RunContinuationsAsynchronously);

	public Task WhenReady => Task.WhenAll(_caches.Task, _variables.Task);

	public Task WhenCachesReady => _caches.Task;

	public Task WhenIconPacksReady => _iconPacks.Task;

	public bool IsReady => _caches.Task.IsCompletedSuccessfully && _variables.Task.IsCompletedSuccessfully;

	public void MarkCachesReady() => _caches.TrySetResult();

	public void MarkVariablesReady() => _variables.TrySetResult();

	public void MarkIconPacksReady() => _iconPacks.TrySetResult();

	public void MarkIconPacksFailed(Exception exception) => _iconPacks.TrySetException(exception);
}
