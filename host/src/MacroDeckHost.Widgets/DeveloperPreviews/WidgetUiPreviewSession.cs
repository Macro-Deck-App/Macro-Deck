using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Previews;
using MacroDeck.Ui.Runtime;

namespace MacroDeckHost.Widgets.DeveloperPreviews;

// Disposal releases whatever the scenario handed over and detaches the view's handlers, which is what
// stops a mock's subscription the moment the preview does - see the identical SDK-side
// MacroDeck.Plugin.Hosting.Capabilities.Ui.UiPreviewSession this mirrors.
internal sealed class WidgetUiPreviewSession : IUiSession
{
	private readonly UiPreviewInstance _instance;
	private readonly UiView _view;
	private readonly EventHandler _onChanged;
	private readonly EventHandler<UiHandlerFaultEventArgs> _onFaulted;

	public WidgetUiPreviewSession(UiPreviewInstance instance)
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
