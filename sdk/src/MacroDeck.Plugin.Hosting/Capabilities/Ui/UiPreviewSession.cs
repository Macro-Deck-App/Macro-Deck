using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Previews;
using MacroDeck.Ui.Runtime;

namespace MacroDeck.Plugin.Hosting.Capabilities.Ui;

internal sealed class UiPreviewSession : IUiSession, IRebuildableUiSession
{
	private readonly Func<UiPreviewInstance> _build;
	private readonly EventHandler _onChanged;
	private readonly EventHandler<UiHandlerFaultEventArgs> _onFaulted;
	private UiPreviewInstance _instance;

	public UiPreviewSession(Func<UiPreviewInstance> build)
	{
		ArgumentNullException.ThrowIfNull(build);

		_build = build;
		_onChanged = (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_onFaulted = (_, fault)
			=> Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));

		_instance = build();
		Subscribe(_instance.View);
	}

	public event EventHandler? Changed;

	public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

	public UiTree BuildTree() => _instance.View.Tree;

	public IReadOnlyList<UiPatch> DrainPatches() => _instance.View.DrainPatches();

	public void Dispatch(UiEvent uiEvent) => _instance.View.Dispatch(uiEvent);

	public IAsyncDisposable Rebuild()
	{
		var next = _build();

		// Everything the new view queued while being built is already in its tree, which the caller
		// publishes as a snapshot next.
		next.View.DrainPatches();

		var previous = _instance;
		Unsubscribe(previous.View);
		_instance = next;
		Subscribe(next.View);

		return previous;
	}

	public ValueTask DisposeAsync()
	{
		Unsubscribe(_instance.View);

		return _instance.DisposeAsync();
	}

	private void Subscribe(UiView view)
	{
		view.Changed += _onChanged;
		view.HandlerFaulted += _onFaulted;
	}

	private void Unsubscribe(UiView view)
	{
		view.Changed -= _onChanged;
		view.HandlerFaulted -= _onFaulted;
	}
}
