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

	public static readonly TimeSpan DoubleTapWindow = TimeSpan.FromMilliseconds(400);

	private readonly TimeProvider _timeProvider;
	private readonly Func<string, Task<DevicePressClaim>> _claim;
	private readonly Func<string, string, DevicePressClaim, Task> _execute;
	private readonly Lock _sync = new();
	private readonly Dictionary<string, PressState> _presses = new(StringComparer.Ordinal);
	private readonly Dictionary<string, TapState> _taps = new(StringComparer.Ordinal);
	private readonly Func<string, bool> _doubleTapEnabled;

	public DeviceSurfacePressTracker(
		TimeProvider timeProvider,
		Func<string, Task<DevicePressClaim>> claim,
		Func<string, string, DevicePressClaim, Task> execute,
		Func<string, bool>? doubleTapEnabled = null)
	{
		_timeProvider = timeProvider;
		_claim = claim;
		_execute = execute;
		_doubleTapEnabled = doubleTapEnabled ?? (_ => false);
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
			if (_taps.TryGetValue(widgetId, out var tap) && tap.Second is null)
			{
				tap.Timer?.Dispose();
				tap.Timer = null;
				tap.Second = state;
				state.Tail = tap.First.Tail;
			}

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
			DropTaps();
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
			DropTaps();
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
			var tapped = released && !state.LongPressFired;
			var doubleTapEnabled = tapped && _doubleTapEnabled(widgetId);
			var completion = TapCompletion.None;
			(Task Previous, TaskCompletionSource Done)? heldShortPress = null;
			(Task Previous, TaskCompletionSource Done) touchEnd;
			(PressState Owner, (Task Previous, TaskCompletionSource Done) Slot)? flushed = null;

			// A press that began between Take and this lock is adopted as the second tap in HoldShortPress,
			// so the short press this release holds cannot be lost to it.
			lock (_sync)
			{
				var isSecond = _taps.TryGetValue(widgetId, out var tap) && ReferenceEquals(tap.Second, state);
				if (isSecond)
				{
					_taps.Remove(widgetId);
				}

				if (isSecond && released && !doubleTapEnabled)
				{
					heldShortPress = Reserve(state);
				}

				touchEnd = Reserve(state);

				completion = (isSecond, doubleTapEnabled, tapped) switch
				{
					(true, true, _) => TapCompletion.Double,
					(false, true, _) => TapCompletion.Held,
					(_, _, true) => TapCompletion.Single,
					_ => TapCompletion.None,
				};

				if (completion == TapCompletion.Held)
				{
					flushed = HoldShortPress(widgetId, state);
				}
			}

			if (heldShortPress is { } held)
			{
				await RunAsync(widgetId, state, WidgetTriggerTypes.ShortPress, held);
			}

			if (flushed is { } stale)
			{
				_ = RunAsync(widgetId, stale.Owner, WidgetTriggerTypes.ShortPress, stale.Slot);
			}

			await RunAsync(widgetId, state, WidgetTriggerTypes.TouchEnd, touchEnd);

			if (completion == TapCompletion.Single)
			{
				await Enqueue(widgetId, state, WidgetTriggerTypes.ShortPress);
			}
			else if (completion == TapCompletion.Double)
			{
				await Enqueue(widgetId, state, WidgetTriggerTypes.DoublePress);
			}
		}
		finally
		{
			await (await state.Claim).DisposeAsync();
		}
	}

	private (PressState Owner, (Task Previous, TaskCompletionSource Done) Slot)? HoldShortPress(
		string widgetId,
		PressState first)
	{
		(PressState, (Task, TaskCompletionSource))? flushed = null;
		if (_taps.Remove(widgetId, out var previous))
		{
			previous.Timer?.Dispose();
			flushed = (previous.First, Reserve(previous.First));
		}

		var tap = new TapState(first);
		if (_presses.TryGetValue(widgetId, out var active))
		{
			tap.Second = active;
		}
		else
		{
			tap.Timer = _timeProvider.CreateTimer(_ => OnDoubleTapWindowElapsed(widgetId, tap),
				null,
				DoubleTapWindow,
				Timeout.InfiniteTimeSpan);
		}

		_taps[widgetId] = tap;
		return flushed;
	}

	private void OnDoubleTapWindowElapsed(string widgetId, TapState tap)
	{
		(Task Previous, TaskCompletionSource Done) slot;
		lock (_sync)
		{
			if (!_taps.TryGetValue(widgetId, out var current) || !ReferenceEquals(current, tap) || tap.Second is not null)
			{
				return;
			}

			_taps.Remove(widgetId);
			tap.Timer?.Dispose();
			slot = Reserve(tap.First);
		}

		_ = RunAsync(widgetId, tap.First, WidgetTriggerTypes.ShortPress, slot);
	}

	private void DropTaps()
	{
		foreach (var tap in _taps.Values)
		{
			tap.Timer?.Dispose();
		}

		_taps.Clear();
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
		(Task Previous, TaskCompletionSource Done)? heldShortPress = null;
		lock (_sync)
		{
			if (!_presses.TryGetValue(widgetId, out var current) || !ReferenceEquals(current, state))
			{
				return;
			}

			state.LongPressFired = true;
			state.Timer?.Dispose();
			state.Timer = null;

			if (_taps.TryGetValue(widgetId, out var tap) && ReferenceEquals(tap.Second, state))
			{
				_taps.Remove(widgetId);
				heldShortPress = Reserve(state);
			}

			// Reserved under the same lock a release takes the press with, so the long press can never land
			// behind that release's touch end or after its claim was closed.
			slot = Reserve(state);
		}

		if (heldShortPress is { } held)
		{
			_ = RunAsync(widgetId, state, WidgetTriggerTypes.ShortPress, held);
		}

		_ = RunAsync(widgetId, state, WidgetTriggerTypes.LongPress, slot);
	}

	private enum TapCompletion
	{
		None,
		Single,
		Double,
		Held,
	}

	private sealed class TapState(PressState first)
	{
		public PressState First { get; } = first;

		public PressState? Second;

		public ITimer? Timer;
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
