using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Runtime;

namespace MacroDeckHost.Widgets.Configuration;

/// <summary>
/// Carries a widget configuration view and its events. Every built-in widget's <c>config</c> surface needs
/// exactly this and nothing else: the tree is built once from the widget's stored data into
/// <see cref="UiState{T}" /> cells the tree's own bindings and visibility already react to, so no widget-specific
/// dispatch logic exists to put here - unlike a widget's own rendering session (see
/// <c>ClockWidgetSession</c>'s remarks for the same shape used there).
/// </summary>
internal sealed class WidgetConfigSession : IUiSession
{
	private readonly UiView _view;
	private Action? _onDispose;

	public WidgetConfigSession(UiView view, Action? onDispose = null)
	{
		ArgumentNullException.ThrowIfNull(view);

		_view = view;
		_onDispose = onDispose;
		_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_view.HandlerFaulted += (_, fault)
			=> Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));
	}

	public event EventHandler? Changed;

	public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

	public UiTree BuildTree() => _view.Tree;

	public IReadOnlyList<UiPatch> DrainPatches() => _view.DrainPatches();

	public void Dispatch(UiEvent uiEvent) => _view.Dispatch(uiEvent);

	public ValueTask DisposeAsync()
	{
		Interlocked.Exchange(ref _onDispose, null)?.Invoke();

		return ValueTask.CompletedTask;
	}
}
