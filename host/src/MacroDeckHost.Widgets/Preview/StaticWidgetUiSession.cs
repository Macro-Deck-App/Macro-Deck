using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Runtime;

namespace MacroDeckHost.Widgets.Preview;

/// <summary>
/// A session over a view that never changes again: what a sample preview needs, and the whole reason a
/// sample can be served without the notifiers, pollers and history windows a live widget's session
/// subscribes to. Those subscriptions are what would overwrite the sample with the host's real (empty)
/// state a moment after it was drawn.
/// </summary>
internal sealed class StaticWidgetUiSession : IUiSession
{
	private readonly UiView _view;

	public StaticWidgetUiSession(UiView view)
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
