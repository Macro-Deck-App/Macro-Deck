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
	private readonly Func<string, string, Task> _execute;
	private readonly Lock _sync = new();
	private readonly Dictionary<string, PressState> _presses = new(StringComparer.Ordinal);

	public DeviceSurfacePressTracker(TimeProvider timeProvider, Func<string, string, Task> execute)
	{
		_timeProvider = timeProvider;
		_execute = execute;
	}

	/// <summary>
	/// Opens a press. A widget already held is ignored outright - no second <c>onTouchStart</c>, and the
	/// long-press timer keeps running from the first press rather than being re-armed.
	/// </summary>
	public Task PressAsync(string widgetId)
	{
		lock (_sync)
		{
			if (_presses.ContainsKey(widgetId))
			{
				return Task.CompletedTask;
			}

			var state = new PressState();
			state.Timer = _timeProvider.CreateTimer(_ => OnLongPressElapsed(widgetId, state),
				null,
				LongPressThreshold,
				Timeout.InfiniteTimeSpan);
			_presses[widgetId] = state;
		}

		return _execute(widgetId, WidgetTriggerTypes.TouchStart);
	}

	public async Task ReleaseAsync(string widgetId)
	{
		if (Take(widgetId) is not { } state)
		{
			return;
		}

		await _execute(widgetId, WidgetTriggerTypes.TouchEnd);
		if (!state.LongPressFired)
		{
			await _execute(widgetId, WidgetTriggerTypes.ShortPress);
		}
	}

	/// <summary>
	/// Ends a press the device never released - navigation away, the device going offline, the session
	/// closing. Emits <c>onTouchEnd</c> only, and disarms the timer so no phantom long press fires later.
	/// </summary>
	public Task CancelAsync(string widgetId)
		=> Take(widgetId) is null ? Task.CompletedTask : _execute(widgetId, WidgetTriggerTypes.TouchEnd);

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
			}

			_presses.Clear();
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
		lock (_sync)
		{
			if (!_presses.TryGetValue(widgetId, out var current) || !ReferenceEquals(current, state))
			{
				return;
			}

			state.LongPressFired = true;
			state.Timer?.Dispose();
			state.Timer = null;
		}

		_ = _execute(widgetId, WidgetTriggerTypes.LongPress);
	}

	private sealed class PressState
	{
		public ITimer? Timer;

		public bool LongPressFired;
	}
}
