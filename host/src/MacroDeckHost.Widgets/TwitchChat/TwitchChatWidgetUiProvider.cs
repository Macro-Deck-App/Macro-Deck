using System.Text.Json;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Twitch.Chat;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Localization;
using MacroDeckHost.Widgets.Configuration;
using MacroDeckHost.Widgets.Preview;

namespace MacroDeckHost.Widgets.TwitchChat;

public sealed class TwitchChatWidgetUiProvider : IBuiltInIntegrationUiProvider
{
	private readonly ITwitchChatFeed _feed;
	private readonly ITwitchChatImages _images;
	private readonly IWidgetSampleTextResolver _text;
	private readonly IIntegrationRegistry _integrations;
	private readonly IUiResourceStore _resources;
	private readonly Lazy<UiResource?> _icon;

	public TwitchChatWidgetUiProvider(ITwitchChatFeed feed,
		ITwitchChatImages images,
		IWidgetSampleTextResolver text,
		IIntegrationRegistry integrations,
		IUiResourceStore resources)
	{
		_feed = feed;
		_images = images;
		_text = text;
		_integrations = integrations;
		_resources = resources;
		_icon = new Lazy<UiResource?>(RegisterIcon);
	}

	public string IntegrationId => TwitchChatWidgetType.OwnerId;

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

		var surface = request.Surface;

		if (surface.Kind == UiSurfaceKinds.Config)
		{
			if (!WidgetConfigSurfaces.IsFor(surface, TwitchChatWidgetType.QualifiedId))
			{
				return null;
			}

			var configView = new UiView(surface,
				TwitchChatWidgetConfigView.Build(WidgetConfigSurfaces.Data(surface), _feed.Accounts));

			return new WidgetConfigSession(configView);
		}

		if (surface.Kind is not (UiSurfaceKinds.Widget or UiSurfaceKinds.Preview) ||
			ReadString(surface, UiWidgetSurfaceAttributes.WidgetType) != TwitchChatWidgetType.QualifiedId)
		{
			return null;
		}

		var separator = await _text.ResolveAsync(AppStrings.Integrations.Twitch.ChatWidget.NameSeparator())
			.ConfigureAwait(false);
		var cornerRadius = WidgetSafeArea.RadiusOf(surface);

		if (WidgetSamplePreview.IsRequested(surface))
		{
			var sample = await TwitchChatWidgetSample.BuildAsync(_text, separator).ConfigureAwait(false);

			return new StaticWidgetUiSession(new UiView(surface,
				TwitchChatWidgetView.Build(new UiState<TwitchChatViewState>(sample), cornerRadius, _icon.Value)));
		}

		var accountId = ReadAccount(surface);
		var lines = new TwitchChatLines(separator, _images);
		var snapshot = _feed.Snapshot(accountId);
		var state = new UiState<TwitchChatViewState>(lines.Build(snapshot));
		var view = new UiView(surface, TwitchChatWidgetView.Build(state, cornerRadius, _icon.Value));

		return new TwitchChatWidgetSession(view, state, lines, _feed, accountId, snapshot.Account?.UserId);
	}

	private UiResource? RegisterIcon()
	{
		if (_integrations.Integrations.FirstOrDefault(integration => integration.Id == IntegrationId) is not
			IIntegrationIconProvider provider)
		{
			return null;
		}

		return _resources.Register(new UiResourceRegistration
		{
			OwnerId = TwitchChatWidgetType.OwnerId,
			Name = "header-icon",
			MediaType = provider.IconMimeType,
			Content = provider.GetIcon(),
		});
	}

	private static string? ReadAccount(UiSurface surface)
		=> surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.Data, out var data) &&
			WidgetConfigJson.ReadString(data, TwitchChatWidgetType.AccountKey) is { Length: > 0 } account
				? account
				: null;

	private static string? ReadString(UiSurface surface, string name)
		=> surface.Attributes.TryGetValue(name, out var element) && element.ValueKind == JsonValueKind.String
			? element.GetString()
			: null;
}
