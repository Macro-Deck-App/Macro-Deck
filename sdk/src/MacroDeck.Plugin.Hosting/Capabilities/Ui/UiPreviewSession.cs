using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Previews;
using MacroDeck.Ui.Runtime;

namespace MacroDeck.Plugin.Hosting.Capabilities.Ui;

// Disposal releases whatever the scenario handed over and detaches the view's handlers. The view itself
// owns nothing disposable - its cells die with it - but a view that faults after the session closed must
// not raise into it, and a mock's subscription must stop the moment the preview does.
internal sealed class UiPreviewSession : IUiSession
{
	private readonly UiPreviewInstance _instance;
	private readonly UiView _view;
	private readonly EventHandler _onChanged;
	private readonly EventHandler<UiHandlerFaultEventArgs> _onFaulted;

	public UiPreviewSession(UiPreviewInstance instance)
	{
		ArgumentNullException.ThrowIfNull(instance);

		_instance = instance;
		_view = instance.View;
		_onChanged = (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_onFaulted = (_, fault)
			=> Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));

		_view.Changed += _onChanged;
		_view.HandlerFaulted += _onFaulted;
	}

	public event EventHandler? Changed;

	public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

	public UiTree BuildTree() => _view.Tree;

	public IReadOnlyList<UiPatch> DrainPatches() => _view.DrainPatches();

	public void Dispatch(UiEvent uiEvent) => _view.Dispatch(uiEvent);

	public ValueTask DisposeAsync()
	{
		_view.Changed -= _onChanged;
		_view.HandlerFaulted -= _onFaulted;

		return _instance.DisposeAsync();
	}
}
