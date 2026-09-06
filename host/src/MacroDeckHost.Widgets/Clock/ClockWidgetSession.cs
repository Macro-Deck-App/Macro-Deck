using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Runtime;

namespace MacroDeckHost.Widgets.Clock;

// No state, no notifier and no scheduler: a clock's tree is built once and every reader advances it
// against its own synchronised clock, so this session exists only to carry the view and its events.
internal sealed class ClockWidgetSession : IUiSession
{
	private readonly UiView _view;

	public ClockWidgetSession(UiView view)
	{
		ArgumentNullException.ThrowIfNull(view);

		_view = view;
		_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_view.HandlerFaulted += (_, fault)
			=> Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));
	}

	public event EventHandler? Changed;

	public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

	public UiTree BuildTree() => _view.Tree;

	public IReadOnlyList<UiPatch> DrainPatches() => _view.DrainPatches();

	public void Dispatch(UiEvent uiEvent) => _view.Dispatch(uiEvent);

	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
