using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Application.Ui.Transport.Messages.Weather;
using MacroDeckHost.Application.Weather;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Widgets.Weather;

/// <summary>
/// Serves the Weather integration's detail dialog. The first-party worked example of an action opening a
/// modal: the action names a view, and the tree is built and kept live here exactly as a widget's is.
/// </summary>
public sealed class WeatherDetailsUiProvider : IBuiltInIntegrationUiProvider
{
	/// <summary>The view id the Weather integration's action asks for.</summary>
	public const string DetailsViewId = "weather-details";

	private const string InstanceIdKey = "instanceId";

	private readonly IWeatherRegistry _registry;
	private readonly IUiResourceStore _resources;
	private readonly IWeatherStateNotifier _notifier;
	private readonly ILogger _logger;

	public WeatherDetailsUiProvider(
		IWeatherRegistry registry,
		IUiResourceStore resources,
		IWeatherStateNotifier notifier,
		ILogger logger)
	{
		_registry = registry;
		_resources = resources;
		_notifier = notifier;
		_logger = logger.ForContext<WeatherDetailsUiProvider>();
	}

	// Spelled here rather than referenced: MacroDeckHost.Integrations sits below this project, and the
	// widget's own icon registration already carries the same literal for the same reason.
	public string IntegrationId => WeatherWidgetIcons.OwnerId;

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
	[
		new()
			{ Kind = UiSurfaceKinds.Dialog, SessionMode = UiSessionModes.Exclusive },
	];

	public async Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		if (request.Surface.Kind is not UiSurfaceKinds.Dialog || ViewId(request.Surface) != DetailsViewId)
		{
			return null;
		}

		var instanceId = InstanceId(request.Surface);
		var icons = WeatherWidgetIcons.EnsureRegistered(_resources);

		var initial = await LoadAsync(instanceId, cancellationToken).ConfigureAwait(false);
		var state = new UiAsyncState<WeatherStatePayload>(ct => LoadAsync(instanceId, ct), initial);

		var view = new UiView(request.Surface, WeatherDetailsView.Build(state, icons));

		// The widget's session, reused rather than duplicated: a dialog needs exactly the same thing from
		// it - refresh the state when this station changes, and relay the view's patches and faults.
		// Bound to the *resolved* instance rather than to the configured one, so a modal opened without a
		// location still follows the station it actually picked.
		return new WeatherWidgetSession(view, _notifier, state, initial.InstanceId);
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
#pragma warning disable CA1031 // A station is plugin-owned; its fault must leave a readable dialog, not fail the session.
		catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
		{
			_logger.Error(exception, "Failed to read weather state for instance '{InstanceId}'", instanceId);

			return WeatherStatePayload.Unavailable(instanceId);
		}
	}

	private static string? ViewId(UiSurface surface)
		=> surface.Attributes.TryGetValue(UiDialogSurfaceAttributes.ViewId, out var value) &&
			value.ValueKind == JsonValueKind.String
				? value.GetString()
				: null;

	private static string? InstanceId(UiSurface surface)
	{
		if (!surface.Attributes.TryGetValue(UiDialogSurfaceAttributes.Data, out var data) ||
			data.ValueKind != JsonValueKind.Object ||
			!data.TryGetProperty(InstanceIdKey, out var instanceId) ||
			instanceId.ValueKind != JsonValueKind.String)
		{
			return null;
		}

		var value = instanceId.GetString();

		return string.IsNullOrEmpty(value) ? null : value;
	}
}
