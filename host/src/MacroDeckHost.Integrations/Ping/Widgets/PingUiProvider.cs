using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Widgets;
using MacroDeckHost.Integrations.Ping.Connections;
using MacroDeckHost.Integrations.Ui;

namespace MacroDeckHost.Integrations.Ping.Widgets;

internal sealed class PingUiProvider : IUiProvider
{
	private readonly IPingTargets _targets;

	public PingUiProvider(IPingTargets targets)
	{
		_targets = targets;
	}

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
	[
		new() { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
		new() { Kind = UiSurfaceKinds.Preview, SessionMode = UiSessionModes.Shared },
		new() { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive }
	];

	public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		var surface = request.Surface;

		if (UiWidgetSurface.IsConfigurationFor(surface, PingWidgetType.QualifiedId))
		{
			return Task.FromResult<IUiSession?>(new UiViewSession(new UiView(surface,
				PingWidgetConfigView.Build(UiWidgetSurface.Data(surface), _targets.Targets))));
		}

		if (!UiWidgetSurface.IsFor(surface, PingWidgetType.QualifiedId))
		{
			return Task.FromResult<IUiSession?>(null);
		}

		var data = UiWidgetSurface.Data(surface);
		var options = PingWidgetSettings.Parse(data);
		var background = PingWidgetSettings.BackgroundColor(data);

		if (UiWidgetSurface.IsSampleRequested(surface))
		{
			var sample = new UiState<PingViewState>(PingWidgetSample.Build(options));
			return Task.FromResult<IUiSession?>(new UiViewSession(new UiView(surface,
				PingWidgetView.Build(sample, UiWidgetSafeArea.RadiusOf(surface), background))));
		}

		return Task.FromResult<IUiSession?>(new PingWidgetSession(surface, options, _targets, background));
	}
}
