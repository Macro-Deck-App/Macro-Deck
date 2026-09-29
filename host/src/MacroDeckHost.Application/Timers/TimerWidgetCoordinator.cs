using System.Collections.Concurrent;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.HostLocking;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace MacroDeckHost.Application.Timers;

public sealed class TimerWidgetCoordinator
{
	private readonly TimerWidgetStore _store;
	private readonly TimerWidgetVariableWriter _variables;
	private readonly IWidgetTriggerService _triggers;
	private readonly ICountdownDurationPrompt _prompt;
	private readonly IFolderCache _folders;
	private readonly IHostLockState _lockState;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILogger _logger;
	private readonly ConcurrentDictionary<(Guid WidgetId, string ClientId), CancellationTokenSource> _prompts = new();

	public TimerWidgetCoordinator(
		TimerWidgetStore store,
		TimerWidgetVariableWriter variables,
		IWidgetTriggerService triggers,
		ICountdownDurationPrompt prompt,
		IFolderCache folders,
		IHostLockState lockState,
		IServiceScopeFactory scopeFactory,
		ILogger logger)
	{
		_store = store;
		_variables = variables;
		_triggers = triggers;
		_prompt = prompt;
		_folders = folders;
		_lockState = lockState;
		_scopeFactory = scopeFactory;
		_logger = logger.ForContext<TimerWidgetCoordinator>();
	}

	public TimerWidgetStore Store => _store;

	// Runs off the caller's thread: a widget session dispatches under its own lock, and the store notifies
	// every other session of the widget, so running the gesture inline could take two session locks crosswise.
	public void Post(Guid widgetId, TimerGesture gesture, string? originClientId, Guid? originDeviceId)
		=> _ = Task.Run(async () =>
		{
			try
			{
				await HandleGestureAsync(widgetId, gesture, originClientId, originDeviceId).ConfigureAwait(false);
			}
#pragma warning disable CA1031 // A press has nobody to report to; the failure is logged instead of lost.
			catch (Exception exception)
#pragma warning restore CA1031
			{
				_logger.Warning(exception, "A {Gesture} on timer widget {WidgetId} failed", gesture, widgetId);
			}
		});

	public async Task<bool> HandleGestureAsync(Guid widgetId,
		TimerGesture gesture,
		string? originClientId,
		Guid? originDeviceId)
	{
		if (_lockState.IsLocked)
		{
			return false;
		}

		if (FindWidget(widgetId) is not { } widget || TimerWidgetConfig.FromWidget(widget) is not { } config)
		{
			return true;
		}

		_store.Ensure(widgetId, config);
		var transitions = _store.Gesture(widgetId, gesture);

		// The widget may have been deleted while this press was on its way.
		if (FindWidget(widgetId) is null)
		{
			await ForgetAsync(widgetId).ConfigureAwait(false);

			return true;
		}

		foreach (var transition in transitions)
		{
			if (transition.NeedsDuration)
			{
				await AskForDurationAsync(transition.Snapshot, originClientId, originDeviceId).ConfigureAwait(false);
			}
			else
			{
				await ApplyAsync(transition, originClientId, originDeviceId).ConfigureAwait(false);
			}
		}

		return true;
	}

	public async Task StartWithDurationAsync(Guid widgetId, int seconds, string? originClientId, Guid? originDeviceId)
	{
		if (_lockState.IsLocked ||
			FindWidget(widgetId) is not { } widget ||
			TimerWidgetConfig.FromWidget(widget) is not { AsksForDuration: true } config)
		{
			return;
		}

		_store.Ensure(widgetId, config);

		if (_store.StartWithDuration(widgetId, seconds) is { } transition)
		{
			await ApplyAsync(transition, originClientId, originDeviceId).ConfigureAwait(false);
		}
	}

	public Task ApplyTickAsync(TimerWidgetTransition transition) => ApplyAsync(transition, null, null);

	public async Task SyncAsync(WidgetEntity widget)
	{
		ArgumentNullException.ThrowIfNull(widget);

		var previous = _store.Get(widget.Id);

		if (TimerWidgetConfig.FromWidget(widget) is { } config)
		{
			if (previous is not null && previous.Kind != config.Kind)
			{
				await _variables.RemoveAsync(widget.Id, previous.Kind).ConfigureAwait(false);
			}

			await _variables.WriteAsync(_store.Ensure(widget.Id, config)).ConfigureAwait(false);
		}
		else if (previous is not null)
		{
			await ForgetAsync(widget.Id).ConfigureAwait(false);
		}
	}

	public async Task SyncAllAsync()
	{
		foreach (var widget in _folders.GetAllFolders().SelectMany(folder => folder.Widgets))
		{
			if (TimerWidgetConfig.IsTimerType(widget.Type))
			{
				await SyncAsync(widget).ConfigureAwait(false);
			}
		}
	}

	public async Task ForgetAsync(Guid widgetId)
	{
		var previous = _store.Get(widgetId);

		CancelPrompts(widgetId, exceptClientId: null);
		_store.Remove(widgetId);
		_variables.Forget(widgetId);

		if (previous is not null)
		{
			await _variables.RemoveAsync(widgetId, previous.Kind).ConfigureAwait(false);
		}
	}

	public async Task PruneMissingAsync()
	{
		var present = _folders.GetAllFolders()
			.SelectMany(folder => folder.Widgets)
			.Select(widget => widget.Id)
			.ToHashSet();

		foreach (var widgetId in _store.WidgetIds.Where(id => !present.Contains(id)))
		{
			await ForgetAsync(widgetId).ConfigureAwait(false);

			await using var scope = _scopeFactory.CreateAsyncScope();
			await scope.ServiceProvider.GetRequiredService<Services.IVariableService>()
				.DeleteByScopeInstance(VariableScope.Widget, widgetId.ToString())
				.ConfigureAwait(false);
		}
	}

	private async Task AskForDurationAsync(TimerWidgetSnapshot snapshot, string? originClientId, Guid? originDeviceId)
	{
		if (string.IsNullOrEmpty(originClientId))
		{
			if (snapshot.LastEnteredSeconds is { } lastEntered)
			{
				await StartWithDurationAsync(snapshot.WidgetId, lastEntered, null, originDeviceId).ConfigureAwait(false);
			}

			return;
		}

		var key = (snapshot.WidgetId, originClientId);
		var cancellation = new CancellationTokenSource();

		// A repeated press from the same client replaces its dialog, so an open event the client never
		// received cannot leave that client unable to start the countdown.
		if (_prompts.TryRemove(key, out var previous))
		{
			await previous.CancelAsync().ConfigureAwait(false);
		}

		_prompts[key] = cancellation;
		_ = PromptAsync(key, cancellation, snapshot.LastEnteredSeconds);
	}

	private async Task PromptAsync((Guid WidgetId, string ClientId) key,
		CancellationTokenSource cancellation,
		int? initialSeconds)
	{
		try
		{
			var seconds = await _prompt.AskAsync(key.WidgetId, key.ClientId, initialSeconds, cancellation.Token)
				.ConfigureAwait(false);

			if (seconds is { } answer && !cancellation.IsCancellationRequested)
			{
				await StartWithDurationAsync(key.WidgetId, answer, key.ClientId, null).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException)
		{
		}
#pragma warning disable CA1031 // A press has nobody to report a dialog failure to; it is logged instead.
		catch (Exception exception)
#pragma warning restore CA1031
		{
			_logger.Warning(exception, "The countdown duration dialog failed for widget {WidgetId}", key.WidgetId);
		}
		finally
		{
			// Not disposed: another press may be cancelling this source at the same moment, and a source
			// without a timer holds nothing that needs releasing.
			_prompts.TryRemove(new KeyValuePair<(Guid, string), CancellationTokenSource>(key, cancellation));
		}
	}

	private async Task ApplyAsync(TimerWidgetTransition transition, string? originClientId, Guid? originDeviceId)
	{
		var snapshot = transition.Snapshot;

		if (snapshot.Phase != TimerWidgetPhase.Idle)
		{
			CancelPrompts(snapshot.WidgetId, exceptClientId: originClientId);
		}

		// The variables are written before the flows run, so a flow starts on the state its trigger reports.
		await _variables.WriteAsync(snapshot).ConfigureAwait(false);

		foreach (var trigger in transition.Triggers)
		{
			if (trigger == WidgetTriggerTypes.CountdownFinished)
			{
				_ = RunHostTriggerAsync(snapshot.WidgetId, trigger);
			}
			else
			{
				_ = RunPressTriggerAsync(snapshot.WidgetId, trigger, originClientId, originDeviceId);
			}
		}
	}

	private void CancelPrompts(Guid widgetId, string? exceptClientId)
	{
		foreach (var (key, cancellation) in _prompts)
		{
			if (key.WidgetId == widgetId && key.ClientId != exceptClientId && _prompts.TryRemove(key, out _))
			{
				cancellation.Cancel();
			}
		}
	}

	private async Task RunPressTriggerAsync(Guid widgetId, string trigger, string? originClientId, Guid? originDeviceId)
	{
		if (FindWidget(widgetId) is not { } widget)
		{
			return;
		}

		try
		{
			await _triggers.ExecuteAsync(widget, trigger, originClientId, originDeviceId, CancellationToken.None)
				.ConfigureAwait(false);
		}
#pragma warning disable CA1031 // The timer already moved; a failing flow is the flow's problem and is logged.
		catch (Exception exception)
#pragma warning restore CA1031
		{
			_logger.Warning(exception, "{Trigger} flow of widget {WidgetId} failed", trigger, widgetId);
		}
	}

	private async Task RunHostTriggerAsync(Guid widgetId, string trigger)
	{
		if (FindWidget(widgetId) is not { } widget)
		{
			return;
		}

		try
		{
			await using var scope = _scopeFactory.CreateAsyncScope();

			// Host-initiated like onStateChange: no client pressed anything, so the host-lock gate does not apply.
			var result = await scope.ServiceProvider.GetRequiredService<IFlowExecutor>()
				.ExecuteAsync(new FlowExecutionRequest
					{
						FlowsSource = widget.Data,
						Trigger = TriggerSelector.ByType(trigger),
						Scope = VariableScope.Widget,
						ScopeRefId = widgetId.ToString(),
						OwnerWidgetId = widgetId,
						Origin = ExecutionOrigin.Host,
					},
					CancellationToken.None)
				.ConfigureAwait(false);

			if (result.Status != FlowExecutionStatus.Succeeded)
			{
				_logger.Warning("{Trigger} flow of widget {WidgetId} finished as {Status}: {ErrorCode}",
					trigger,
					widgetId,
					result.Status,
					result.ErrorCode);
			}
		}
#pragma warning disable CA1031 // See RunPressTriggerAsync.
		catch (Exception exception)
#pragma warning restore CA1031
		{
			_logger.Warning(exception, "{Trigger} flow of widget {WidgetId} failed", trigger, widgetId);
		}
	}

	private WidgetEntity? FindWidget(Guid widgetId)
		=> _folders.GetAllFolders().SelectMany(folder => folder.Widgets).FirstOrDefault(widget => widget.Id == widgetId);
}
