using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.HostLocking;
using MacroDeckHost.Application.Ui.Transport;
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
	private readonly IWidgetTriggerService _triggerService;
	private readonly IFolderCache _folderCache;
	private readonly IUiTransport _uiTransport;

	public SliderWidgetUiProvider(IWidgetIconResources iconResources,
		IHostLockState lockState,
		IWidgetSampleTextResolver sampleText,
		TimeProvider timeProvider,
		VariableRegistry variables,
		IVariableChangeNotifier variableNotifier,
		IServiceScopeFactory scopeFactory,
		IWidgetTriggerService triggerService,
		IFolderCache folderCache,
		IUiTransport uiTransport)
	{
		_triggerService = triggerService;
		_folderCache = folderCache;
		_uiTransport = uiTransport;
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
			var configWidgetId = WidgetConfigSurfaces.WidgetId(request.Surface);
			var configured = SliderWidgetData.Parse(configData);

			if (configured.ValueVariable is null && configWidgetId is { } configOwner)
			{
				await SliderDefaultVariable.EnsureAsync(configOwner, configured.Min, _variables, _scopeFactory)
					.ConfigureAwait(false);
			}

			var configView = new UiView(request.Surface,
				SliderWidgetConfigView.Build(configData, _variables, configWidgetId));

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

		var scopeWidgetId = SurfaceGuid(request.Surface,
			isWidgetSurface ? UiWidgetSurfaceAttributes.WidgetId : UiWidgetSurfaceAttributes.VariableScopeWidgetId);

		if (isWidgetSurface && config.ValueVariable is null && scopeWidgetId is { } owner)
		{
			await SliderDefaultVariable.EnsureAsync(owner, config.Min, _variables, _scopeFactory).ConfigureAwait(false);
		}

		var variable = config.ValueVariable is not null || scopeWidgetId is not null
			? new SliderVariableBinding(config.ValueVariable ?? SliderDefaultVariable.Name,
				config.Min,
				config.Max,
				config.Step,
				_variables,
				_variableNotifier,
				_scopeFactory,
				scopeWidgetId,
				IsDefault: config.ValueVariable is null)
			: null;

		var doublePress = isWidgetSurface && config.HasDoublePressFlow && scopeWidgetId is { } widgetId
			? new SliderDoublePressBinding(widgetId, _folderCache, _triggerService, _uiTransport)
			: null;

		var session = new SliderWidgetSession(state, _lockState, _timeProvider, isWidgetSurface, variable, doublePress);

		var element = SliderWidgetView.Build(variable is null ? config : config with { ValueVariable = variable.Name },
			state,
			icon,
			session.BuildEvents(),
			WidgetSafeArea.RadiusOf(request.Surface));
		var view = new UiView(request.Surface, element);

		session.Attach(view);

		return session;
	}

	private static Guid? SurfaceGuid(UiSurface surface, string attribute)
		=> surface.Attributes.TryGetValue(attribute, out var id) &&
			id.ValueKind == JsonValueKind.String &&
			Guid.TryParse(id.GetString(), out var widgetId)
				? widgetId
				: null;

	private static JsonElement DataElement(UiSurface surface)
		=> surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.Data, out var data) ? data : default;
}
