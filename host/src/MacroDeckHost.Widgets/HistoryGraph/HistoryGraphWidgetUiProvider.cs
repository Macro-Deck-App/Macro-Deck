using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Widgets.Configuration;
using MacroDeckHost.Widgets.Preview;

namespace MacroDeckHost.Widgets.HistoryGraph;

public sealed class HistoryGraphWidgetUiProvider : IBuiltInWidgetUiProvider
{
	private readonly VariableRegistry _variables;
	private readonly IVariableHistory _history;
	private readonly IVariableChangeNotifier _notifier;
	private readonly IWidgetSampleTextResolver _sampleText;

	public HistoryGraphWidgetUiProvider(VariableRegistry variables,
		IVariableHistory history,
		IVariableChangeNotifier notifier,
		IWidgetSampleTextResolver sampleText)
	{
		_variables = variables;
		_history = history;
		_notifier = notifier;
		_sampleText = sampleText;
	}

	public string WidgetTypeId => WidgetTypeIds.HistoryGraph;

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
	[
		new()
			{ Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
		new()
			{ Kind = UiSurfaceKinds.Preview, SessionMode = UiSessionModes.Shared },
		new()
			{ Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
	];

	public async Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		if (request.Surface.Kind == UiSurfaceKinds.Config)
		{
			if (!WidgetConfigSurfaces.IsFor(request.Surface, WidgetTypeId))
			{
				return null;
			}

			var configData = WidgetConfigSurfaces.Data(request.Surface);
			var configView = new UiView(request.Surface, HistoryGraphWidgetConfigView.Build(configData));

			return new WidgetConfigSession(configView);
		}

		if (request.Surface.Kind is not (UiSurfaceKinds.Widget or UiSurfaceKinds.Preview))
		{
			return null;
		}

		var config = HistoryGraphWidgetData.Parse(DataElement(request.Surface));

		if (WidgetSamplePreview.IsRequested(request.Surface))
		{
			var (sampleConfig, sampleState) = await HistoryGraphWidgetSample.BuildAsync(config, _sampleText)
				.ConfigureAwait(false);

			return new StaticWidgetUiSession(new UiView(request.Surface,
				HistoryGraphWidgetView.Build(new UiState<HistoryGraphViewState>(sampleState), sampleConfig)));
		}

		var resolver = new HistoryGraphViewStateResolver(config, _variables, VariableScopeWidgetId(request.Surface));
		var window = string.IsNullOrEmpty(config.ValueVariable)
			? EmptyVariableHistoryWindow.Instance
			: _history.Open(config.ValueVariable, config.HistoryLength);

		var state = new UiState<HistoryGraphViewState>(resolver.Resolve(window.Values));
		var view = new UiView(request.Surface,
			HistoryGraphWidgetView.Build(state, config, WidgetSafeArea.RadiusOf(request.Surface)));

		return new HistoryGraphWidgetSession(view, state, resolver, window, _notifier, config);
	}

	private static string? VariableScopeWidgetId(UiSurface surface)
		=> ReadStringAttribute(surface, UiWidgetSurfaceAttributes.WidgetId) ??
			ReadStringAttribute(surface, UiWidgetSurfaceAttributes.VariableScopeWidgetId);

	private static string? ReadStringAttribute(UiSurface surface, string name)
		=> surface.Attributes.TryGetValue(name, out var element) && element.ValueKind == JsonValueKind.String
			? element.GetString()
			: null;

	private static JsonElement DataElement(UiSurface surface)
		=> surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.Data, out var data) ? data : default;
}
