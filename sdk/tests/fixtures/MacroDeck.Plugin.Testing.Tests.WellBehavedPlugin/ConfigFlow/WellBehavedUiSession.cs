using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Runtime;

namespace MacroDeck.Plugin.Testing.Tests.WellBehavedPlugin.ConfigFlow;

/// <summary>
/// Forwards a <see cref="UiView"/> authored with the <c>MacroDeck.Ui</c> DSL to the model-only
/// <see cref="IUiSession"/> contract a provider returns - see the "Serve a view to Macro Deck" section of
/// the UI guide. Reused by both the config flow tree (<see cref="WellBehavedConfigFlow"/>) and the
/// configurable action tree (<c>SetAlertThresholdAction</c>), which is why it lives beside the flow rather
/// than inside either one.
/// </summary>
internal sealed class WellBehavedUiSession : IUiSession
{
	private readonly UiView _view;

	public WellBehavedUiSession(UiView view)
	{
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
