using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Variables;

namespace MacroDeckHost.Widgets.Gauges;

internal sealed class GaugesWidgetSession : IUiSession
{
	private readonly UiView _view;
	private readonly IReadOnlyList<GaugeConfig> _gauges;
	private readonly IReadOnlyList<UiState<GaugeFace>> _faces;
	private readonly GaugesViewStateResolver _resolver;
	private readonly IVariableChangeNotifier _variables;
	private readonly Dictionary<string, List<int>> _watched = new(StringComparer.Ordinal);
	private readonly Lock _refreshSync = new();

	public GaugesWidgetSession(UiView view,
		IReadOnlyList<GaugeConfig> gauges,
		IReadOnlyList<UiState<GaugeFace>> faces,
		GaugesViewStateResolver resolver,
		IVariableChangeNotifier variables)
	{
		ArgumentNullException.ThrowIfNull(view);
		ArgumentNullException.ThrowIfNull(gauges);
		ArgumentNullException.ThrowIfNull(faces);
		ArgumentNullException.ThrowIfNull(resolver);
		ArgumentNullException.ThrowIfNull(variables);

		_view = view;
		_gauges = gauges;
		_faces = faces;
		_resolver = resolver;
		_variables = variables;

		for (var i = 0; i < gauges.Count; i++)
		{
			var names = WidgetVariableReferenceParser.ReferencedNames(gauges[i].Name).ToHashSet(StringComparer.Ordinal);

			if (!string.IsNullOrEmpty(gauges[i].Variable))
			{
				names.Add(gauges[i].Variable);
			}

			foreach (var name in names)
			{
				if (!_watched.TryGetValue(name, out var indices))
				{
					_watched[name] = indices = [];
				}

				indices.Add(i);
			}
		}

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
		_variables.Changed -= OnVariableChanged;

		return ValueTask.CompletedTask;
	}

	private void OnVariableChanged(object? sender, VariableChangedEventArgs args)
	{
		if (!_watched.TryGetValue(args.Name, out var indices))
		{
			return;
		}

		lock (_refreshSync)
		{
			foreach (var index in indices)
			{
				_faces[index].Set(_resolver.Resolve(_gauges[index]));
			}
		}
	}
}
