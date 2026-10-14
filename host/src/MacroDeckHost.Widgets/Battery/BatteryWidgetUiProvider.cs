using System.Text.Json;
using MacroDeck.Sdk.Batteries;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Batteries;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Widgets.Configuration;
using MacroDeckHost.Widgets.Preview;

namespace MacroDeckHost.Widgets.Battery;

public sealed class BatteryWidgetUiProvider : IBuiltInWidgetUiProvider
{
	private readonly IBatteryApi _batteries;
	private readonly IWidgetSampleTextResolver _sampleText;

	public BatteryWidgetUiProvider(IBatteryRegistry batteries, IWidgetSampleTextResolver sampleText)
		: this((IBatteryApi)batteries, sampleText)
	{
	}

	internal BatteryWidgetUiProvider(IBatteryApi batteries, IWidgetSampleTextResolver sampleText)
	{
		ArgumentNullException.ThrowIfNull(batteries);
		ArgumentNullException.ThrowIfNull(sampleText);

		_batteries = batteries;
		_sampleText = sampleText;
	}

	public string WidgetTypeId => WidgetTypeIds.Battery;

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
	[
		new()
			{ Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
		new()
			{ Kind = UiSurfaceKinds.Preview, SessionMode = UiSessionModes.Shared },
		new()
			{ Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive }
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

			return new WidgetConfigSession(new UiView(request.Surface,
				BatteryWidgetConfigView.Build(WidgetConfigSurfaces.Data(request.Surface), _batteries)));
		}

		if (request.Surface.Kind is not (UiSurfaceKinds.Widget or UiSurfaceKinds.Preview))
		{
			return null;
		}

		var config = BatteryWidgetData.Parse(DataElement(request.Surface));
		var cornerRadius = WidgetSafeArea.RadiusOf(request.Surface);

		if (WidgetSamplePreview.IsRequested(request.Surface))
		{
			var sample = await BatteryWidgetSample.BuildAsync(config, _sampleText).ConfigureAwait(false);

			return new StaticWidgetUiSession(new UiView(request.Surface,
				BatteryWidgetView.Build(new UiState<BatteryViewState>(sample), config, cornerRadius)));
		}

		var resolver = new BatteryViewStateResolver(_batteries);
		var state = new UiState<BatteryViewState>(resolver.Resolve(config));
		var view = new UiView(request.Surface, BatteryWidgetView.Build(state, config, cornerRadius));

		return new BatteryWidgetSession(view, state, config, resolver, _batteries);
	}

	private static JsonElement DataElement(UiSurface surface)
	{
		return surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.Data, out var data) ? data : default;
	}
}
