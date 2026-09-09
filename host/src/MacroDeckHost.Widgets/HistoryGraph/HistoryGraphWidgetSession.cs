using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Variables;

namespace MacroDeckHost.Widgets.HistoryGraph;

internal sealed class HistoryGraphWidgetSession : IUiSession
{
	private readonly UiView _view;
	private readonly UiState<HistoryGraphViewState> _state;
	private readonly HistoryGraphViewStateResolver _resolver;
	private readonly IVariableHistoryWindow _window;
	private readonly IVariableChangeNotifier _variables;
	private readonly HashSet<string> _watched;

	public HistoryGraphWidgetSession(UiView view,
		UiState<HistoryGraphViewState> state,
		HistoryGraphViewStateResolver resolver,
		IVariableHistoryWindow window,
		IVariableChangeNotifier variables,
		HistoryGraphWidgetData config)
	{
		ArgumentNullException.ThrowIfNull(view);
		ArgumentNullException.ThrowIfNull(state);
		ArgumentNullException.ThrowIfNull(resolver);
		ArgumentNullException.ThrowIfNull(window);
		ArgumentNullException.ThrowIfNull(variables);
		ArgumentNullException.ThrowIfNull(config);

		_view = view;
		_state = state;
		_resolver = resolver;
		_window = window;
		_variables = variables;
		_watched = new HashSet<string>(StringComparer.Ordinal);

		if (!string.IsNullOrEmpty(config.ValueVariable))
		{
			_watched.Add(config.ValueVariable);
		}

		_watched.UnionWith(WidgetVariableReferenceParser.ReferencedNames(config.Subtitle));

		_window.Changed += OnSampled;
		_variables.Changed += OnVariableChanged;
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
		_window.Changed -= OnSampled;
		_variables.Changed -= OnVariableChanged;
		_window.Dispose();

		return ValueTask.CompletedTask;
	}

	private void OnSampled(object? sender, EventArgs args) => Refresh();

	private void OnVariableChanged(object? sender, VariableChangedEventArgs args)
	{
		if (_watched.Contains(args.Name))
		{
			Refresh();
		}
	}

	// One resolve feeds every property the tree reads, and an unchanged resolve compares equal - so a flat
	// metric between two samples costs a comparison and emits no patch at all.
	private void Refresh() => _state.Set(_resolver.Resolve(_window.Values));
}
