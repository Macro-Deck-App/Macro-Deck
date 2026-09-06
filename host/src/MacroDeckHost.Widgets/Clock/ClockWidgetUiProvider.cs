using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Widgets.Configuration;

namespace MacroDeckHost.Widgets.Clock;

public sealed class ClockWidgetUiProvider : IBuiltInWidgetUiProvider
{
	public string WidgetTypeId => WidgetTypeIds.Clock;

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
	[
		new()
			{ Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
		new()
			{ Kind = UiSurfaceKinds.Preview, SessionMode = UiSessionModes.Shared },
		new()
			{ Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
	];

	public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		if (request.Surface.Kind is UiSurfaceKinds.Widget or UiSurfaceKinds.Preview)
		{
			var config = ClockWidgetData.Parse(DataElement(request.Surface));
			var view = new UiView(request.Surface,
				ClockWidgetView.Build(config, WidgetSafeArea.RadiusOf(request.Surface)));

			return Task.FromResult<IUiSession?>(new ClockWidgetSession(view));
		}

		if (request.Surface.Kind == UiSurfaceKinds.Config && WidgetConfigSurfaces.IsFor(request.Surface, WidgetTypeId))
		{
			var data = WidgetConfigSurfaces.Data(request.Surface);
			var configView = new UiView(request.Surface, ClockWidgetConfigView.Build(data));

			return Task.FromResult<IUiSession?>(new WidgetConfigSession(configView));
		}

		return Task.FromResult<IUiSession?>(null);
	}

	private static JsonElement DataElement(UiSurface surface)
		=> surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.Data, out var data) ? data : default;
}
