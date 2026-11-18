using MacroDeckHost.Application.Triggers;

namespace MacroDeckHost.Application.Services;

public sealed class StartupReadiness
{
	private readonly TaskCompletionSource _caches = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly TaskCompletionSource _variables = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly TaskCompletionSource _iconPacks = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly TaskCompletionSource _eventDispatch = new(TaskCreationOptions.RunContinuationsAsynchronously);

	private readonly TaskCompletionSource _startupEventsDispatched =
		new(TaskCreationOptions.RunContinuationsAsynchronously);

	private readonly Lock _heldGate = new();
	private List<EventOccurrence>? _held = [];

	public Task WhenReady => Task.WhenAll(_caches.Task, _variables.Task);

	public Task WhenCachesReady => _caches.Task;

	public Task WhenIconPacksReady => _iconPacks.Task;

	public Task WhenEventDispatchReady => _eventDispatch.Task;

	public Task WhenStartupEventsDispatched => _startupEventsDispatched.Task;

	public bool AreStartupEventsDispatched => _startupEventsDispatched.Task.IsCompletedSuccessfully;

	public bool IsReady => _caches.Task.IsCompletedSuccessfully && _variables.Task.IsCompletedSuccessfully;

	public void MarkCachesReady()
	{
		_caches.TrySetResult();
	}

	public void MarkVariablesReady()
	{
		_variables.TrySetResult();
	}

	public void MarkIconPacksReady()
	{
		_iconPacks.TrySetResult();
	}

	public void MarkIconPacksFailed(Exception exception)
	{
		_iconPacks.TrySetException(exception);
	}

	public bool HoldUntilEventDispatchReady(EventOccurrence occurrence)
	{
		lock (_heldGate)
		{
			_held?.Add(occurrence);
			return _held is not null;
		}
	}

	public void MarkEventDispatchReady(Action<EventOccurrence> publishHeld)
	{
		// Published under the gate, so an occurrence raised right after the release cannot overtake a held one.
		lock (_heldGate)
		{
			foreach (var occurrence in _held ?? [])
			{
				publishHeld(occurrence);
			}

			_held = null;
		}

		_eventDispatch.TrySetResult();
	}

	public void MarkStartupEventsDispatched()
	{
		_startupEventsDispatched.TrySetResult();
	}
}
