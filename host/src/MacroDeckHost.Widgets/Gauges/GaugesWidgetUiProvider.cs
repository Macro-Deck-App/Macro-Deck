using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Widgets.Configuration;
using MacroDeckHost.Widgets.Preview;

namespace MacroDeckHost.Widgets.Gauges;

public sealed class GaugesWidgetUiProvider : IBuiltInWidgetUiProvider
{
	private readonly VariableRegistry _variables;
	private readonly IVariableChangeNotifier _notifier;
	private readonly IWidgetIconResources _iconResources;
	private readonly IWidgetSampleTextResolver _sampleText;

	public GaugesWidgetUiProvider(VariableRegistry variables,
		IVariableChangeNotifier notifier,
		IWidgetIconResources iconResources,
		IWidgetSampleTextResolver sampleText)
	{
		_variables = variables;
		_notifier = notifier;
		_iconResources = iconResources;
		_sampleText = sampleText;
	}

	public string WidgetTypeId => WidgetTypeIds.Gauges;

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

			var configView = new UiView(request.Surface,
				GaugesWidgetConfigView.Build(WidgetConfigSurfaces.Data(request.Surface)));

			return new WidgetConfigSession(configView);
		}

		if (request.Surface.Kind is not (UiSurfaceKinds.Widget or UiSurfaceKinds.Preview))
		{
			return null;
		}

		var config = GaugesWidgetData.Parse(DataElement(request.Surface));
		var cornerRadius = WidgetSafeArea.RadiusOf(request.Surface);

		if (WidgetSamplePreview.IsRequested(request.Surface))
		{
			var (sampleConfig, sampleFaces) = await GaugesWidgetSample.BuildAsync(config, _sampleText)
				.ConfigureAwait(false);

			var sampleIcons = await ResolveIconsAsync(sampleConfig.Shown, cancellationToken).ConfigureAwait(false);

			return new StaticWidgetUiSession(new UiView(request.Surface,
				GaugesWidgetView.Build(sampleConfig, sampleFaces, sampleIcons, cornerRadius)));
		}

		var gauges = config.Shown;
		var icons = await ResolveIconsAsync(gauges, cancellationToken).ConfigureAwait(false);

		var resolver = new GaugesViewStateResolver(_variables, VariableScopeWidgetId(request.Surface));
		var faces = gauges.Select(gauge => new UiState<GaugeFace>(resolver.Resolve(gauge))).ToList();
		var view = new UiView(request.Surface, GaugesWidgetView.Build(config, faces, icons, cornerRadius));

		return new GaugesWidgetSession(view, gauges, faces, resolver, _notifier);
	}

	private async Task<Dictionary<string, UiResource>> ResolveIconsAsync(IReadOnlyList<GaugeConfig> gauges,
		CancellationToken cancellationToken)
	{
		var icons = new Dictionary<string, UiResource>(StringComparer.Ordinal);

		foreach (var gauge in gauges)
		{
			if (await _iconResources.ResolveAsync(gauge.Icon, cancellationToken).ConfigureAwait(false) is { } icon)
			{
				icons[gauge.Id] = icon;
			}
		}

		return icons;
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
