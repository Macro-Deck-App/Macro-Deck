using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Variables;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Widgets;
using MacroDeckHost.Integrations.Ping.Connections;
using MacroDeckHost.Integrations.Ping.State;
using MacroDeckHost.Integrations.Ping.Variables;

namespace MacroDeckHost.Integrations.Ping.Widgets;

internal delegate Task<IVariableHistoryWindow?> PingHistoryOpener(
	string variableName,
	int capacity,
	CancellationToken cancellationToken);

internal sealed class PingWidgetSession : IUiSession
{
	private readonly PingWidgetOptions _options;
	private readonly IPingTargets _targets;
	private readonly PingHistoryOpener _history;
	private readonly UiState<PingViewState> _state;
	private readonly UiView _view;
	private readonly Lock _sync = new();

	private IVariableHistoryWindow? _window;
	private (string Name, int Capacity)? _bound;
	private int _windowGeneration;
	private bool _disposed;

	public PingWidgetSession(
		UiSurface surface,
		PingWidgetOptions options,
		IPingTargets targets,
		PingHistoryOpener history,
		string? backgroundColor)
	{
		ArgumentNullException.ThrowIfNull(surface);
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(targets);
		ArgumentNullException.ThrowIfNull(history);

		_options = options;
		_targets = targets;
		_history = history;
		_state = new UiState<PingViewState>(PingViewStateResolver.Resolve(options,
			PingViewStateResolver.Target(options, targets),
			[]));
		_view = new UiView(surface,
			PingWidgetView.Build(_state, UiWidgetSafeArea.RadiusOf(surface), backgroundColor));
		_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_view.HandlerFaulted += (_, fault)
			=> Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));
		_targets.Changed += OnTargetsChanged;
		Refresh(true);
	}

	public event EventHandler? Changed;

	public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

	public UiTree BuildTree()
	{
		return _view.Tree;
	}

	public IReadOnlyList<UiPatch> DrainPatches()
	{
		return _view.DrainPatches();
	}

	public void Dispatch(UiEvent uiEvent)
	{
		lock (_sync)
		{
			_view.Dispatch(uiEvent);
		}
	}

	public async ValueTask DisposeAsync()
	{
		_targets.Changed -= OnTargetsChanged;

		IVariableHistoryWindow? window;
		lock (_sync)
		{
			_disposed = true;
			window = Detach();
		}

		if (window is not null)
		{
			await window.DisposeAsync().ConfigureAwait(false);
		}

		_view.Dispose();
	}

	private void OnTargetsChanged(object? sender, EventArgs args)
	{
		Refresh(true);
	}

	private void OnSampled(object? sender, EventArgs args)
	{
		Refresh(false);
	}

	private void Refresh(bool rebind)
	{
		IVariableHistoryWindow? released = null;

		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			var target = PingViewStateResolver.Target(_options, _targets);
			if (rebind)
			{
				released = Rebind(target);
			}

			var next = PingViewStateResolver.Resolve(_options, target, _window?.Values ?? []);
			if (next != _state.Value)
			{
				using (_view.Batch())
				{
					_state.Set(next);
				}
			}
		}

		if (released is not null)
		{
			_ = released.DisposeAsync().AsTask();
		}
	}

	private IVariableHistoryWindow? Rebind(PingSnapshot? target)
	{
		(string Name, int Capacity)? wanted = _options.ShowHistory && target is not null
			? (PingVariables.Name(target.Key, PingVariables.Latency), PingViewStateResolver.HistoryCapacity(target))
			: null;

		if (wanted == _bound)
		{
			return null;
		}

		var released = Detach();
		_bound = wanted;

		if (wanted is { } binding)
		{
			_ = OpenHistoryAsync(binding.Name, binding.Capacity, ++_windowGeneration);
		}

		return released;
	}

	private IVariableHistoryWindow? Detach()
	{
		_windowGeneration++;
		_bound = null;
		var window = _window;
		_window = null;

		if (window is not null)
		{
			window.Changed -= OnSampled;
		}

		return window;
	}

	// Started while the session lock is held, so it yields before touching the session again.
	private async Task OpenHistoryAsync(string variableName, int capacity, int generation)
	{
		await Task.Yield();

		IVariableHistoryWindow? window;
		try
		{
			window = await _history(variableName, capacity, CancellationToken.None).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is VariableHistoryException or OperationCanceledException)
		{
			return;
		}

		if (window is null)
		{
			return;
		}

		lock (_sync)
		{
			if (!_disposed && generation == _windowGeneration)
			{
				_window = window;
				window.Changed += OnSampled;
				window = null;
			}
		}

		if (window is not null)
		{
			await window.DisposeAsync().ConfigureAwait(false);
			return;
		}

		Refresh(false);
	}
}
