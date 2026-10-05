using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.StreamChat;
using MacroDeckHost.Application.StreamStats;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Widgets.Configuration;
using MacroDeckHost.Widgets.Preview;

namespace MacroDeckHost.Widgets.StreamStats;

public sealed class StreamStatsWidgetUiProvider
{
	private readonly StreamPlatform _platform;
	private readonly IStreamStatsAccounts _accounts;
	private readonly IStreamThumbnails _thumbnails;
	private readonly VariableRegistry _variables;
	private readonly IVariableHistory _history;
	private readonly IVariableChangeNotifier _notifier;
	private readonly IWidgetSampleTextResolver _text;
	private readonly IIntegrationRegistry _integrations;
	private readonly IUiResourceStore _resources;
	private readonly Lazy<UiResource?> _logo;

	public StreamStatsWidgetUiProvider(
		StreamPlatform platform,
		IStreamStatsAccounts accounts,
		IStreamThumbnails thumbnails,
		VariableRegistry variables,
		IVariableHistory history,
		IVariableChangeNotifier notifier,
		IWidgetSampleTextResolver text,
		IIntegrationRegistry integrations,
		IUiResourceStore resources)
	{
		ArgumentNullException.ThrowIfNull(platform);

		_platform = platform;
		_accounts = accounts;
		_thumbnails = thumbnails;
		_variables = variables;
		_history = history;
		_notifier = notifier;
		_text = text;
		_integrations = integrations;
		_resources = resources;
		_logo = new Lazy<UiResource?>(() => StreamStatsLogo.Register(_platform, _integrations, _resources));
	}

	public string IntegrationId => _platform.OwnerId;

	public bool Serves(UiSurface surface)
	{
		ArgumentNullException.ThrowIfNull(surface);

		return surface.Kind switch
		{
			UiSurfaceKinds.Config => WidgetConfigSurfaces.IsFor(surface, _platform.StatsWidgetTypeId),
			UiSurfaceKinds.Widget or UiSurfaceKinds.Preview
				=> ReadString(surface, UiWidgetSurfaceAttributes.WidgetType) == _platform.StatsWidgetTypeId,
			_ => false,
		};
	}

	public async Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		var surface = request.Surface;

		if (!Serves(surface))
		{
			return null;
		}

		if (surface.Kind == UiSurfaceKinds.Config)
		{
			return new WidgetConfigSession(new UiView(surface,
				StreamStatsWidgetConfigView.Build(_platform, WidgetConfigSurfaces.Data(surface), _accounts.Accounts)));
		}

		var data = surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.Data, out var element) ? element : default;
		var cornerRadius = WidgetSafeArea.RadiusOf(surface);
		var backgroundColor = StreamStatsWidgetSettings.BackgroundColor(data);
		var options = StreamStatsWidgetSettings.Options(_platform, data);

		if (WidgetSamplePreview.IsRequested(surface))
		{
			var sample = await StreamStatsWidgetSample.BuildAsync(_platform, _text).ConfigureAwait(false);

			return new StaticWidgetUiSession(new UiView(surface,
				StreamStatsWidgetView.Build(_platform,
					new UiState<StreamStatsViewState>(sample),
					options,
					cornerRadius,
					_logo.Value,
					backgroundColor)));
		}

		var state = new UiState<StreamStatsViewState>(StreamStatsViewState.Empty);
		var view = new UiView(surface,
			StreamStatsWidgetView.Build(_platform, state, options, cornerRadius, _logo.Value, backgroundColor));

		return new StreamStatsWidgetSession(_platform,
			view,
			state,
			StreamStatsWidgetSettings.Account(data),
			options,
			_accounts,
			_variables,
			_history,
			_notifier,
			_thumbnails);
	}

	private static string? ReadString(UiSurface surface, string name)
		=> surface.Attributes.TryGetValue(name, out var element) && element.ValueKind == JsonValueKind.String
			? element.GetString()
			: null;
}
