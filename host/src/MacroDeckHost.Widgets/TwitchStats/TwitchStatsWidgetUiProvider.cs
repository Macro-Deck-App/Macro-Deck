using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Twitch.Stats;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Widgets.Configuration;
using MacroDeckHost.Widgets.Preview;

namespace MacroDeckHost.Widgets.TwitchStats;

public sealed class TwitchStatsWidgetUiProvider
{
	private readonly ITwitchStatsAccounts _accounts;
	private readonly ITwitchStreamThumbnails _thumbnails;
	private readonly VariableRegistry _variables;
	private readonly IVariableHistory _history;
	private readonly IVariableChangeNotifier _notifier;
	private readonly IWidgetSampleTextResolver _text;
	private readonly IIntegrationRegistry _integrations;
	private readonly IUiResourceStore _resources;
	private readonly Lazy<UiResource?> _logo;

	public TwitchStatsWidgetUiProvider(
		ITwitchStatsAccounts accounts,
		ITwitchStreamThumbnails thumbnails,
		VariableRegistry variables,
		IVariableHistory history,
		IVariableChangeNotifier notifier,
		IWidgetSampleTextResolver text,
		IIntegrationRegistry integrations,
		IUiResourceStore resources)
	{
		_accounts = accounts;
		_thumbnails = thumbnails;
		_variables = variables;
		_history = history;
		_notifier = notifier;
		_text = text;
		_integrations = integrations;
		_resources = resources;
		_logo = new Lazy<UiResource?>(() => TwitchStatsLogo.Register(_integrations, _resources));
	}

	public static bool Serves(UiSurface surface)
	{
		ArgumentNullException.ThrowIfNull(surface);

		return surface.Kind switch
		{
			UiSurfaceKinds.Config => WidgetConfigSurfaces.IsFor(surface, TwitchStatsWidgetType.QualifiedId),
			UiSurfaceKinds.Widget or UiSurfaceKinds.Preview
				=> ReadString(surface, UiWidgetSurfaceAttributes.WidgetType) == TwitchStatsWidgetType.QualifiedId,
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
				TwitchStatsWidgetConfigView.Build(WidgetConfigSurfaces.Data(surface), _accounts.Accounts)));
		}

		var data = surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.Data, out var element) ? element : default;
		var cornerRadius = WidgetSafeArea.RadiusOf(surface);
		var backgroundColor = TwitchStatsWidgetSettings.BackgroundColor(data);
		var options = TwitchStatsWidgetSettings.Options(data);

		if (WidgetSamplePreview.IsRequested(surface))
		{
			var sample = await TwitchStatsWidgetSample.BuildAsync(_text).ConfigureAwait(false);

			return new StaticWidgetUiSession(new UiView(surface,
				TwitchStatsWidgetView.Build(new UiState<TwitchStatsViewState>(sample),
					options,
					cornerRadius,
					_logo.Value,
					backgroundColor)));
		}

		var state = new UiState<TwitchStatsViewState>(TwitchStatsViewState.Empty);
		var view = new UiView(surface,
			TwitchStatsWidgetView.Build(state, options, cornerRadius, _logo.Value, backgroundColor));

		return new TwitchStatsWidgetSession(view,
			state,
			TwitchStatsWidgetSettings.Account(data),
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
