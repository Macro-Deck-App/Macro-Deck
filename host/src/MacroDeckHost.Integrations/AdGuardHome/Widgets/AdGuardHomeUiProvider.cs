using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Widgets;
using MacroDeckHost.Integrations.AdGuardHome.Connections;
using MacroDeckHost.Integrations.Ui;

namespace MacroDeckHost.Integrations.AdGuardHome.Widgets;

internal sealed class AdGuardHomeUiProvider : IUiProvider
{
	private readonly IAdGuardHomeInstances _instances;
	private readonly Func<IUiResourceRegistry?> _resources;
	private readonly TimeProvider _time;

	public AdGuardHomeUiProvider(
		IAdGuardHomeInstances instances,
		Func<IUiResourceRegistry?> resources,
		TimeProvider time)
	{
		_instances = instances;
		_resources = resources;
		_time = time;
	}

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
	[
		new() { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
		new() { Kind = UiSurfaceKinds.Preview, SessionMode = UiSessionModes.Shared },
		new() { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive }
	];

	public async Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		var surface = request.Surface;

		if (UiWidgetSurface.IsConfigurationFor(surface, AdGuardHomeWidgetType.QualifiedId))
		{
			return new UiViewSession(new UiView(surface,
				AdGuardHomeWidgetConfigView.Build(UiWidgetSurface.Data(surface), _instances.Instances)));
		}

		if (!UiWidgetSurface.IsFor(surface, AdGuardHomeWidgetType.QualifiedId))
		{
			return null;
		}

		var data = UiWidgetSurface.Data(surface);
		var options = AdGuardHomeWidgetSettings.Parse(data);
		var background = AdGuardHomeWidgetSettings.BackgroundColor(data);
		var registry = _resources();
		var icons = await AdGuardHomeWidgetIcons.ResolveAsync(registry, cancellationToken).ConfigureAwait(false);

		if (UiWidgetSurface.IsSampleRequested(surface))
		{
			var sample = new UiState<AdGuardHomeViewState>(AdGuardHomeWidgetSample.Build(options, _time.GetUtcNow()));
			return new UiViewSession(new UiView(surface,
				AdGuardHomeWidgetView.Build(sample,
					options,
					new UiState<AdGuardHomeIconResources>(icons),
					AdGuardHomeWidgetCommands.None,
					UiWidgetSafeArea.RadiusOf(surface),
					background)));
		}

		return new AdGuardHomeWidgetSession(surface,
			options,
			_instances,
			_time,
			icons,
			registry is null ? _resources : null,
			background);
	}
}
