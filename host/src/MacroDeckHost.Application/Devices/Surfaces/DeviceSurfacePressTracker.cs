using MacroDeckHost.Domain.Common;

namespace MacroDeckHost.Application.Devices.Surfaces;

/// <summary>
/// Turns a device's raw press and release reports into the widget triggers the Angular client
/// synthesizes from a pointer, so a hardware button and an on-screen button behave identically:
/// <c>onTouchStart</c> on press, <c>onLongPress</c> once the hold passes
/// <see cref="LongPressThreshold" />, and <c>onTouchEnd</c> plus <c>onShortPress</c> on release unless
/// the long press already fired.
/// </summary>
/// <remarks>
/// State is per widget, not per device: a release on one widget must never end another widget's
/// in-flight press. Timing runs on the injected <see cref="TimeProvider" />, never on the real clock.
/// </remarks>
public sealed class DeviceSurfacePressTracker : IDisposable
{
	public static readonly TimeSpan LongPressThreshold = TimeSpan.FromMilliseconds(600);

	private readonly TimeProvider _timeProvider;
	private readonly Func<string, Task<DevicePressClaim>> _claim;
	private readonly Func<string, string, DevicePressClaim, Task> _execute;
	private readonly Lock _sync = new();
	private readonly Dictionary<string, PressState> _presses = new(StringComparer.Ordinal);

	public DeviceSurfacePressTracker(
		TimeProvider timeProvider,
		Func<string, Task<DevicePressClaim>> claim,
		Func<string, string, DevicePressClaim, Task> execute)
	{
		_timeProvider = timeProvider;
		_claim = claim;
		_execute = execute;
	}

	/// <summary>
	/// Opens a press. A widget already held is ignored outright - no second <c>onTouchStart</c>, and the
	/// long-press timer keeps running from the first press rather than being re-armed.
	/// </summary>
	public Task PressAsync(string widgetId)
	{
		PressState state;
		lock (_sync)
		{
			if (_presses.ContainsKey(widgetId))
			{
				return Task.CompletedTask;
			}

			state = new PressState();
			state.Timer = _timeProvider.CreateTimer(_ => OnLongPressElapsed(widgetId, state),
				null,
				LongPressThreshold,
				Timeout.InfiniteTimeSpan);
			_presses[widgetId] = state;
		}

		state.Resolve(_claim(widgetId));
		return Settle(state, Enqueue(widgetId, state, WidgetTriggerTypes.TouchStart));
	}

	public Task ReleaseAsync(string widgetId)
		=> Take(widgetId) is { } state
			? Settle(state, FinishAsync(widgetId, state, released: true))
			: Task.CompletedTask;

	/// <summary>
	/// Ends a press the device never released - navigation away, the device going offline, the session
	/// closing. Emits <c>onTouchEnd</c> only, and disarms the timer so no phantom long press fires later.
	/// </summary>
	public Task CancelAsync(string widgetId)
		=> Take(widgetId) is { } state
			? Settle(state, FinishAsync(widgetId, state, released: false))
			: Task.CompletedTask;

	public async Task CancelAllAsync()
	{
		string[] held;
		lock (_sync)
		{
			held = [.. _presses.Keys];
		}

		foreach (var widgetId in held)
		{
			await CancelAsync(widgetId);
		}
	}

	public void Dispose()
	{
		lock (_sync)
		{
			foreach (var state in _presses.Values)
			{
				state.Timer?.Dispose();
				_ = CloseAfterAsync(state.Tail, state);
			}

			_presses.Clear();
		}
	}

	// A plugin's device reports and its tile's tree share one serial connection, so a press that waited
	// here for the tree would starve the very tree it waits for. The phases still run in order behind it.
	private static Task Settle(PressState state, Task work) => state.Claim.IsCompleted ? work : Task.CompletedTask;

	private static async Task CloseAfterAsync(Task tail, PressState state)
	{
		await tail;
		await (await state.Claim).DisposeAsync();
	}

	private async Task FinishAsync(string widgetId, PressState state, bool released)
	{
		try
		{
			await Enqueue(widgetId, state, WidgetTriggerTypes.TouchEnd);
			if (released && !state.LongPressFired)
			{
				await Enqueue(widgetId, state, WidgetTriggerTypes.ShortPress);
			}
		}
		finally
		{
			await (await state.Claim).DisposeAsync();
		}
	}

	private Task Enqueue(string widgetId, PressState state, string triggerType)
	{
		(Task Previous, TaskCompletionSource Done) slot;
		lock (_sync)
		{
			slot = Reserve(state);
		}

		return RunAsync(widgetId, state, triggerType, slot);
	}

	private static (Task Previous, TaskCompletionSource Done) Reserve(PressState state)
	{
		var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var previous = state.Tail;
		state.Tail = done.Task;
		return (previous, done);
	}

	private async Task RunAsync(
		string widgetId,
		PressState state,
		string triggerType,
		(Task Previous, TaskCompletionSource Done) slot)
	{
		try
		{
			await slot.Previous;
			await _execute(widgetId, triggerType, await state.Claim);
		}
		finally
		{
			slot.Done.TrySetResult();
		}
	}

	private PressState? Take(string widgetId)
	{
		lock (_sync)
		{
			if (!_presses.Remove(widgetId, out var state))
			{
				return null;
			}

			state.Timer?.Dispose();
			return state;
		}
	}

	private void OnLongPressElapsed(string widgetId, PressState state)
	{
		(Task Previous, TaskCompletionSource Done) slot;
		lock (_sync)
		{
			if (!_presses.TryGetValue(widgetId, out var current) || !ReferenceEquals(current, state))
			{
				return;
			}

			state.LongPressFired = true;
			state.Timer?.Dispose();
			state.Timer = null;

			// Reserved under the same lock a release takes the press with, so the long press can never land
			// behind that release's touch end or after its claim was closed.
			slot = Reserve(state);
		}

		_ = RunAsync(widgetId, state, WidgetTriggerTypes.LongPress, slot);
	}

	private sealed class PressState
	{
		private readonly TaskCompletionSource<DevicePressClaim> _claim =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Task<DevicePressClaim> Claim => _claim.Task;

		public Task Tail = Task.CompletedTask;

		public ITimer? Timer;

		public bool LongPressFired;

		public void Resolve(Task<DevicePressClaim> claim) => _ = ResolveAsync(claim);

		private async Task ResolveAsync(Task<DevicePressClaim> claim)
		{
			try
			{
				_claim.TrySetResult(await claim);
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				_claim.TrySetResult(DevicePressClaim.Absorbing(null, null));
			}
		}
	}
}
