using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.HostLocking;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Widgets.Configuration;
using MacroDeckHost.Widgets.Preview;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Widgets.Slider;

public sealed class SliderWidgetUiProvider : IBuiltInWidgetUiProvider
{
	private readonly IWidgetIconResources _iconResources;
	private readonly IHostLockState _lockState;
	private readonly IWidgetSampleTextResolver _sampleText;
	private readonly TimeProvider _timeProvider;
	private readonly VariableRegistry _variables;
	private readonly IVariableChangeNotifier _variableNotifier;
	private readonly IServiceScopeFactory _scopeFactory;

	public SliderWidgetUiProvider(IWidgetIconResources iconResources,
		IHostLockState lockState,
		IWidgetSampleTextResolver sampleText,
		TimeProvider timeProvider,
		VariableRegistry variables,
		IVariableChangeNotifier variableNotifier,
		IServiceScopeFactory scopeFactory)
	{
		_iconResources = iconResources;
		_lockState = lockState;
		_sampleText = sampleText;
		_timeProvider = timeProvider;
		_variables = variables;
		_variableNotifier = variableNotifier;
		_scopeFactory = scopeFactory;
	}

	public string WidgetTypeId => WidgetTypeIds.Slider;

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
			var configView = new UiView(request.Surface, SliderWidgetConfigView.Build(configData, _variables));

			return new WidgetConfigSession(configView);
		}

		if (request.Surface.Kind is not (UiSurfaceKinds.Widget or UiSurfaceKinds.Preview))
		{
			return null;
		}

		var config = SliderWidgetData.Parse(DataElement(request.Surface));

		if (WidgetSamplePreview.IsRequested(request.Surface))
		{
			config = await SliderWidgetSample.BuildAsync(config, _sampleText).ConfigureAwait(false);
		}

		var icon = await _iconResources.ResolveAsync(config.Icon, cancellationToken).ConfigureAwait(false);

		// Neither surface reads before the tree exists: opening the session is what a tile waits on to draw
		// anything at all, so both start empty, read once the session is up, and patch the value in.
		var state = new UiState<SliderWidgetReadout>(SliderWidgetReadout.Empty);
		var isWidgetSurface = request.Surface.Kind == UiSurfaceKinds.Widget;

		var variable = config.ValueVariable is { } name
			? new SliderVariableBinding(name,
				config.Min,
				config.Max,
				config.Step,
				_variables,
				_variableNotifier,
				_scopeFactory)
			: null;

		var session = new SliderWidgetSession(state, _lockState, _timeProvider, isWidgetSurface, variable);

		var element = SliderWidgetView.Build(config,
			state,
			icon,
			session.BuildEvents(),
			WidgetSafeArea.RadiusOf(request.Surface));
		var view = new UiView(request.Surface, element);

		session.Attach(view);

		return session;
	}

	private static JsonElement DataElement(UiSurface surface)
		=> surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.Data, out var data) ? data : default;
}
