using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Application.Ui.Transport.Messages.Weather;
using MacroDeckHost.Application.Weather;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Widgets.Configuration;
using MacroDeckHost.Widgets.Preview;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Widgets.Weather;

public sealed class WeatherWidgetUiProvider : IBuiltInWidgetUiProvider
{
	private readonly IWeatherRegistry _registry;
	private readonly IUiResourceStore _resources;
	private readonly IWeatherStateNotifier _notifier;
	private readonly IWidgetSampleTextResolver _sampleText;
	private readonly ILogger _logger;

	public WeatherWidgetUiProvider(IWeatherRegistry registry,
		IUiResourceStore resources,
		IWeatherStateNotifier notifier,
		IWidgetSampleTextResolver sampleText,
		ILogger logger)
	{
		_registry = registry;
		_resources = resources;
		_notifier = notifier;
		_sampleText = sampleText;
		_logger = logger.ForContext<WeatherWidgetUiProvider>();
	}

	public string WidgetTypeId => WidgetTypeIds.Weather;

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

			var data = WidgetConfigSurfaces.Data(request.Surface);
			var configView = new UiView(request.Surface, WeatherWidgetConfigView.Build(data, _registry));

			return new WidgetConfigSession(configView);
		}

		if (request.Surface.Kind is not (UiSurfaceKinds.Widget or UiSurfaceKinds.Preview))
		{
			return null;
		}

		var config = WeatherWidgetData.Parse(DataElement(request.Surface));
		var icons = WeatherWidgetIcons.EnsureRegistered(_resources);

		if (WidgetSamplePreview.IsRequested(request.Surface))
		{
			var sample = await WeatherWidgetSample.BuildAsync(config, _sampleText).ConfigureAwait(false);
			var sampleState = new UiAsyncState<WeatherStatePayload>(_ => Task.FromResult(sample), sample);

			return new StaticWidgetUiSession(new UiView(request.Surface,
				WeatherWidgetView.Build(sampleState, config, icons)));
		}

		var initial = await LoadAsync(config.InstanceId, cancellationToken).ConfigureAwait(false);
		var state = new UiAsyncState<WeatherStatePayload>(ct => LoadAsync(config.InstanceId, ct), initial);

		var view = new UiView(request.Surface,
			WeatherWidgetView.Build(state, config, icons, WidgetSafeArea.RadiusOf(request.Surface)));

		return new WeatherWidgetSession(view, _notifier, state, config.InstanceId);
	}

	private async Task<WeatherStatePayload> LoadAsync(string? configuredInstanceId, CancellationToken cancellationToken)
	{
		var instances = _registry.GetInstances();
		var instanceId = configuredInstanceId ?? (instances.Count > 0 ? instances[0].InstanceId : null);
		var station = instanceId is null ? null : _registry.GetStation(instanceId);

		if (station is null)
		{
			return WeatherStatePayload.UnknownStation(instanceId);
		}

		try
		{
			var snapshot = await station.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);

			return WeatherStatePayload.From(snapshot, instanceId);
		}
#pragma warning disable CA1031 // A station is plugin-owned; its fault must leave a usable card, not fail the session.
		catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
		{
			_logger.Error(exception, "Failed to read weather state for instance '{InstanceId}'", instanceId);

			return WeatherStatePayload.Unavailable(instanceId);
		}
	}

	private static JsonElement DataElement(UiSurface surface)
		=> surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.Data, out var data) ? data : default;
}
