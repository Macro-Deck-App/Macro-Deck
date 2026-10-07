using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.AdGuardHome;
using MacroDeckHost.Application.HostLocking;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Widgets.Configuration;
using MacroDeckHost.Widgets.Preview;

namespace MacroDeckHost.Widgets.AdGuardHome;

public sealed class AdGuardHomeUiProvider : IBuiltInIntegrationUiProvider
{
	private readonly IAdGuardHomeInstances _instances;
	private readonly IHostLockState _lockState;
	private readonly IUiTransport _transport;
	private readonly IUiResourceStore _resources;
	private readonly IIntegrationRegistry _integrations;
	private readonly TimeProvider _time;

	public AdGuardHomeUiProvider(
		IAdGuardHomeInstances instances,
		IHostLockState lockState,
		IUiTransport transport,
		IUiResourceStore resources,
		IIntegrationRegistry integrations,
		TimeProvider time)
	{
		_instances = instances;
		_lockState = lockState;
		_transport = transport;
		_resources = resources;
		_integrations = integrations;
		_time = time;
	}

	public string IntegrationId => AdGuardHomeWidgetType.OwnerId;

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
	[
		new() { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
		new() { Kind = UiSurfaceKinds.Preview, SessionMode = UiSessionModes.Shared },
		new() { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
	];

	public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		var surface = request.Surface;

		if (surface.Kind == UiSurfaceKinds.Config)
		{
			return Task.FromResult<IUiSession?>(WidgetConfigSurfaces.IsFor(surface, AdGuardHomeWidgetType.QualifiedId)
				? new WidgetConfigSession(new UiView(surface,
					AdGuardHomeWidgetConfigView.Build(WidgetConfigSurfaces.Data(surface), _instances.Instances)))
				: null);
		}

		if (surface.Kind is not (UiSurfaceKinds.Widget or UiSurfaceKinds.Preview) ||
			ReadString(surface, UiWidgetSurfaceAttributes.WidgetType) != AdGuardHomeWidgetType.QualifiedId)
		{
			return Task.FromResult<IUiSession?>(null);
		}

		var data = surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.Data, out var element) &&
			element.ValueKind == JsonValueKind.Object
				? element
				: default;
		var options = AdGuardHomeWidgetSettings.Parse(data);
		var background = AdGuardHomeWidgetSettings.BackgroundColor(data);
		var icons = AdGuardHomeWidgetIcons.EnsureRegistered(_resources, _integrations);

		if (WidgetSamplePreview.IsRequested(surface))
		{
			var sample = new UiState<AdGuardHomeViewState>(AdGuardHomeWidgetSample.Build(options, _time.GetUtcNow()));
			return Task.FromResult<IUiSession?>(new StaticWidgetUiSession(new UiView(surface,
				AdGuardHomeWidgetView.Build(sample,
					options,
					icons,
					AdGuardHomeWidgetCommands.None,
					WidgetSafeArea.RadiusOf(surface),
					background))));
		}

		var widgetId = Guid.TryParse(ReadString(surface, UiWidgetSurfaceAttributes.WidgetId), out var id)
			? id
			: (Guid?)null;

		return Task.FromResult<IUiSession?>(new AdGuardHomeWidgetSession(surface,
			widgetId,
			options,
			_instances,
			_lockState,
			_transport,
			_time,
			icons,
			background));
	}

	private static string? ReadString(UiSurface surface, string name)
		=> surface.Attributes.TryGetValue(name, out var element) && element.ValueKind == JsonValueKind.String
			? element.GetString()
			: null;
}
