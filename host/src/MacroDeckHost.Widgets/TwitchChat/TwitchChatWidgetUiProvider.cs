using System.Text.Json;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.HostLocking;
using MacroDeckHost.Application.Twitch.Chat;
using MacroDeckHost.Application.Ui.Modals;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Localization;
using MacroDeckHost.Widgets.Configuration;
using MacroDeckHost.Widgets.Preview;
using MacroDeckHost.Widgets.TwitchStats;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Widgets.TwitchChat;

public sealed class TwitchChatWidgetUiProvider : IBuiltInIntegrationUiProvider
{
	public const string DialogViewId = "twitch-chat";

	private const string DialogWidgetKey = "widgetId";

	private readonly ITwitchChatFeed _feed;
	private readonly ITwitchChatImages _images;
	private readonly IWidgetSampleTextResolver _text;
	private readonly IIntegrationRegistry _integrations;
	private readonly IUiResourceStore _resources;
	private readonly IUiInteractionsFactory _interactions;
	private readonly IFolderCache _folders;
	private readonly IHostLockState _hostLock;
	private readonly ILogger _logger;
	private readonly TwitchStatsWidgetUiProvider? _stats;
	private readonly Lazy<UiResource?> _icon;

	public TwitchChatWidgetUiProvider(ITwitchChatFeed feed,
		ITwitchChatImages images,
		IWidgetSampleTextResolver text,
		IIntegrationRegistry integrations,
		IUiResourceStore resources,
		IUiInteractionsFactory interactions,
		IFolderCache folders,
		IHostLockState hostLock,
		ILogger logger,
		TwitchStatsWidgetUiProvider? stats = null)
	{
		_feed = feed;
		_images = images;
		_text = text;
		_integrations = integrations;
		_resources = resources;
		_interactions = interactions;
		_folders = folders;
		_hostLock = hostLock;
		_logger = logger.ForContext<TwitchChatWidgetUiProvider>();
		_stats = stats;
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
		new()
			{ Kind = UiSurfaceKinds.Dialog, SessionMode = UiSessionModes.Exclusive },
	];

	public async Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		var surface = request.Surface;

		if (_stats is not null && TwitchStatsWidgetUiProvider.Serves(surface))
		{
			return await _stats.CreateSessionAsync(request, cancellationToken).ConfigureAwait(false);
		}

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

		if (surface.Kind == UiSurfaceKinds.Dialog)
		{
			return ReadString(surface, UiDialogSurfaceAttributes.ViewId) == DialogViewId
				? await CreateDialogAsync(surface).ConfigureAwait(false)
				: null;
		}

		if (surface.Kind is not (UiSurfaceKinds.Widget or UiSurfaceKinds.Preview) ||
			ReadString(surface, UiWidgetSurfaceAttributes.WidgetType) != TwitchChatWidgetType.QualifiedId)
		{
			return null;
		}

		var separator = await _text.ResolveAsync(AppStrings.Integrations.Twitch.ChatWidget.NameSeparator())
			.ConfigureAwait(false);
		var cornerRadius = WidgetSafeArea.RadiusOf(surface);
		var backgroundColor = surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.Data, out var widgetData)
			? TwitchChatWidgetSettings.BackgroundColor(widgetData)
			: null;
		var scale = widgetData.ValueKind == JsonValueKind.Undefined ? 1 : TwitchChatWidgetSettings.FontScale(widgetData);
		var messageColor = TwitchChatWidgetSettings.MessageColor(widgetData);
		var nameColor = TwitchChatWidgetSettings.NameColor(widgetData);
		var layout = TwitchChatLines.WidgetLayout(scale, messageColor, nameColor);

		if (WidgetSamplePreview.IsRequested(surface))
		{
			var sample = await TwitchChatWidgetSample.BuildAsync(_text, separator, layout).ConfigureAwait(false);

			return new StaticWidgetUiSession(new UiView(surface,
				TwitchChatWidgetView.Build(new UiState<TwitchChatViewState>(sample), cornerRadius, _icon.Value,
					backgroundColor: backgroundColor, scale: scale, messageColor: messageColor)));
		}

		var accountId = ReadAccount(surface);
		var lines = new TwitchChatLines(separator, _images, layout);
		var snapshot = _feed.Snapshot(accountId);
		var state = new UiState<TwitchChatViewState>(lines.Build(snapshot));
		var widgetId = surface.Kind == UiSurfaceKinds.Widget ? WidgetId(surface) : null;

		TwitchChatWidgetSession? session = null;
		IReadOnlyList<UiEventHandler> events = widgetId is { } id
			? [UiEventHandler.On(UiComponentEvents.Press, () => OpenDialog(session, id))]
			: [];

		var view = new UiView(surface, TwitchChatWidgetView.Build(state, cornerRadius, _icon.Value, events,
			backgroundColor, scale, messageColor));
		session = new TwitchChatWidgetSession(view, state, lines, _feed, accountId, snapshot.Account?.UserId);

		return session;
	}

	private async Task<IUiSession> CreateDialogAsync(UiSurface surface)
	{
		var data = surface.Attributes.TryGetValue(UiDialogSurfaceAttributes.Data, out var element)
			? element
			: default;
		var accountId = WidgetConfigJson.ReadString(data, TwitchChatWidgetType.AccountKey) is { Length: > 0 } account
			? account
			: null;
		var widgetId = Guid.TryParse(WidgetConfigJson.ReadString(data, DialogWidgetKey), out var parsed)
			? parsed
			: (Guid?)null;

		var separator = await _text.ResolveAsync(AppStrings.Integrations.Twitch.ChatWidget.NameSeparator())
			.ConfigureAwait(false);

		return new TwitchChatDialogSession(surface,
			new TwitchChatLines(separator, _images, TwitchChatDialogView.Layout),
			_feed,
			accountId,
			() => Permission(widgetId, accountId),
			Moderator,
			_hostLock,
			_logger);
	}

	private void OpenDialog(TwitchChatWidgetSession? session, Guid widgetId)
	{
		if (session?.DialogRequest() is not { } request)
		{
			return;
		}

		var data = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		{
			[DialogWidgetKey] = JsonSerializer.SerializeToElement(widgetId.ToString()),
		};

		if (request.AccountId is { Length: > 0 } accountId)
		{
			data[TwitchChatWidgetType.AccountKey] = JsonSerializer.SerializeToElement(accountId);
		}

		var modal = new ModalDefinition
		{
			ViewId = DialogViewId,
			Title = request.AccountLabel is { } label
				? AppStrings.Integrations.Twitch.ChatWidget.Title(account: label)
				: AppStrings.Integrations.Twitch.ChatWidget.Name(),
			Data = data,
		};

		_ = ShowDialogAsync(request.OriginClientId, modal);
	}

	private async Task ShowDialogAsync(string originClientId, ModalDefinition modal)
	{
		try
		{
			if (!await _interactions.ForIntegration(IntegrationId).ShowModalAsync(originClientId, modal)
					.ConfigureAwait(false))
			{
				_logger.Warning("The Twitch chat dialog could not be opened on client {ClientId}", originClientId);
			}
		}
#pragma warning disable CA1031 // A press has nobody to report to; the fault is logged instead of lost.
		catch (Exception exception)
#pragma warning restore CA1031
		{
			_logger.Warning(exception, "Failed to open the Twitch chat dialog on client {ClientId}", originClientId);
		}
	}

	private TwitchChatWidgetPermission Permission(Guid? widgetId, string? accountId)
	{
		var widget = widgetId is { } id
			? _folders.GetAllFolders().SelectMany(folder => folder.Widgets).FirstOrDefault(candidate => candidate.Id == id)
			: null;

		if (widget is null || accountId is null)
		{
			return TwitchChatWidgetPermission.TurnedOff;
		}

		using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(widget.Data) ? "{}" : widget.Data);

		if (!TwitchChatWidgetSettings.AllowsModeration(document.RootElement))
		{
			return TwitchChatWidgetPermission.TurnedOff;
		}

		var configured = TwitchChatWidgetSettings.Account(document.RootElement);
		var resolved = configured ?? _feed.Snapshot(null).Account?.UserId;

		return string.Equals(resolved, accountId, StringComparison.Ordinal)
			? TwitchChatWidgetPermission.Allowed
			: TwitchChatWidgetPermission.AccountChanged;
	}

	private ITwitchChatModerator? Moderator()
		=> _integrations.Integrations.FirstOrDefault(integration =>
			integration is ITwitchChatModerator && _integrations.IsEnabled(integration.Id)) as ITwitchChatModerator;

	private static Guid? WidgetId(UiSurface surface)
		=> Guid.TryParse(ReadString(surface, UiWidgetSurfaceAttributes.WidgetId), out var id) ? id : null;

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
		=> surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.Data, out var data)
			? TwitchChatWidgetSettings.Account(data)
			: null;

	private static string? ReadString(UiSurface surface, string name)
		=> surface.Attributes.TryGetValue(name, out var element) && element.ValueKind == JsonValueKind.String
			? element.GetString()
			: null;
}
