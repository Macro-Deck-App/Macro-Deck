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
using MacroDeckHost.Application.StreamChat;
using MacroDeckHost.Application.Ui.Modals;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Localization;
using MacroDeckHost.Widgets.Configuration;
using MacroDeckHost.Widgets.Preview;
using MacroDeckHost.Widgets.StreamStats;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Widgets.StreamChat;

public sealed class StreamChatWidgetUiProvider : IBuiltInIntegrationUiProvider
{
	private const string DialogWidgetKey = "widgetId";

	private readonly StreamPlatform _platform;
	private readonly IStreamChatFeed _feed;
	private readonly ITwitchChatImages? _images;
	private readonly IWidgetSampleTextResolver _text;
	private readonly IIntegrationRegistry _integrations;
	private readonly IUiResourceStore _resources;
	private readonly IUiInteractionsFactory _interactions;
	private readonly IFolderCache _folders;
	private readonly IHostLockState _hostLock;
	private readonly ILogger _logger;
	private readonly StreamStatsWidgetUiProvider? _stats;
	private readonly Lazy<UiResource?> _icon;

	public StreamChatWidgetUiProvider(StreamPlatform platform,
		IStreamChatFeed feed,
		ITwitchChatImages? images,
		IWidgetSampleTextResolver text,
		IIntegrationRegistry integrations,
		IUiResourceStore resources,
		IUiInteractionsFactory interactions,
		IFolderCache folders,
		IHostLockState hostLock,
		ILogger logger,
		StreamStatsWidgetUiProvider? stats = null)
	{
		ArgumentNullException.ThrowIfNull(platform);

		if (stats is not null && !string.Equals(stats.IntegrationId, platform.OwnerId, StringComparison.Ordinal))
		{
			throw new ArgumentException($"The stats provider serves {stats.IntegrationId}, not {platform.OwnerId}",
				nameof(stats));
		}

		_platform = platform;
		_feed = feed;
		_images = images;
		_text = text;
		_integrations = integrations;
		_resources = resources;
		_interactions = interactions;
		_folders = folders;
		_hostLock = hostLock;
		_logger = logger.ForContext<StreamChatWidgetUiProvider>();
		_stats = stats;
		_icon = new Lazy<UiResource?>(RegisterIcon);
	}

	public string IntegrationId => _platform.OwnerId;

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

		if (_stats is not null && _stats.Serves(surface))
		{
			return await _stats.CreateSessionAsync(request, cancellationToken).ConfigureAwait(false);
		}

		if (surface.Kind == UiSurfaceKinds.Config)
		{
			if (!WidgetConfigSurfaces.IsFor(surface, _platform.ChatWidgetTypeId))
			{
				return null;
			}

			var configView = new UiView(surface,
				StreamChatWidgetConfigView.Build(_platform, WidgetConfigSurfaces.Data(surface), _feed.Accounts));

			return new WidgetConfigSession(configView);
		}

		if (surface.Kind == UiSurfaceKinds.Dialog)
		{
			return ReadString(surface, UiDialogSurfaceAttributes.ViewId) == _platform.DialogViewId
				? await CreateDialogAsync(surface).ConfigureAwait(false)
				: null;
		}

		if (surface.Kind is not (UiSurfaceKinds.Widget or UiSurfaceKinds.Preview) ||
			ReadString(surface, UiWidgetSurfaceAttributes.WidgetType) != _platform.ChatWidgetTypeId)
		{
			return null;
		}

		var separator = await _text.ResolveAsync(AppStrings.Integrations.StreamChat.Widget.NameSeparator())
			.ConfigureAwait(false);
		var cornerRadius = WidgetSafeArea.RadiusOf(surface);
		var backgroundColor = surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.Data, out var widgetData)
			? StreamChatWidgetSettings.BackgroundColor(widgetData)
			: null;
		var scale = widgetData.ValueKind == JsonValueKind.Undefined ? 1 : StreamChatWidgetSettings.FontScale(widgetData);
		var layout = StreamChatLines.WidgetLayout(scale);

		if (WidgetSamplePreview.IsRequested(surface))
		{
			var sample = await StreamChatWidgetSample.BuildAsync(_text, separator, layout).ConfigureAwait(false);

			return new StaticWidgetUiSession(new UiView(surface,
				StreamChatWidgetView.Build(_platform,
					new UiState<StreamChatViewState>(sample),
					cornerRadius,
					_icon.Value,
					backgroundColor: backgroundColor,
					scale: scale)));
		}

		var accountId = ReadAccount(surface);
		var lines = new StreamChatLines(separator, _images, layout);
		var snapshot = _feed.Snapshot(accountId);
		var state = new UiState<StreamChatViewState>(lines.Build(snapshot));
		var widgetId = surface.Kind == UiSurfaceKinds.Widget ? WidgetId(surface) : null;

		StreamChatWidgetSession? session = null;
		IReadOnlyList<UiEventHandler> events = widgetId is { } id
			? [UiEventHandler.On(UiComponentEvents.Press, () => OpenDialog(session, id))]
			: [];

		var view = new UiView(surface, StreamChatWidgetView.Build(_platform, state, cornerRadius, _icon.Value, events,
			backgroundColor, scale));
		session = new StreamChatWidgetSession(view, state, lines, _feed, accountId, snapshot.Account?.AccountId);

		return session;
	}

	private async Task<IUiSession> CreateDialogAsync(UiSurface surface)
	{
		var data = surface.Attributes.TryGetValue(UiDialogSurfaceAttributes.Data, out var element)
			? element
			: default;
		var accountId = WidgetConfigJson.ReadString(data, StreamChatWidgetType.AccountKey) is { Length: > 0 } account
			? account
			: null;
		var widgetId = Guid.TryParse(WidgetConfigJson.ReadString(data, DialogWidgetKey), out var parsed)
			? parsed
			: (Guid?)null;

		var separator = await _text.ResolveAsync(AppStrings.Integrations.StreamChat.Widget.NameSeparator())
			.ConfigureAwait(false);

		return new StreamChatDialogSession(_platform,
			surface,
			new StreamChatLines(separator, _images, StreamChatDialogView.Layout),
			_feed,
			accountId,
			() => Permission(widgetId, accountId),
			Moderator,
			_hostLock,
			_logger);
	}

	private void OpenDialog(StreamChatWidgetSession? session, Guid widgetId)
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
			data[StreamChatWidgetType.AccountKey] = JsonSerializer.SerializeToElement(accountId);
		}

		var modal = new ModalDefinition
		{
			ViewId = _platform.DialogViewId,
			Title = request.AccountLabel is { } label ? _platform.Chat.Title(label) : _platform.Chat.Name(),
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
				_logger.Warning("The {Platform} chat dialog could not be opened on client {ClientId}",
					_platform.OwnerId,
					originClientId);
			}
		}
#pragma warning disable CA1031 // A press has nobody to report to; the fault is logged instead of lost.
		catch (Exception exception)
#pragma warning restore CA1031
		{
			_logger.Warning(exception,
				"Failed to open the {Platform} chat dialog on client {ClientId}",
				_platform.OwnerId,
				originClientId);
		}
	}

	private StreamChatWidgetPermission Permission(Guid? widgetId, string? accountId)
	{
		var widget = widgetId is { } id
			? _folders.GetAllFolders().SelectMany(folder => folder.Widgets).FirstOrDefault(candidate => candidate.Id == id)
			: null;

		if (widget is null || accountId is null)
		{
			return StreamChatWidgetPermission.TurnedOff;
		}

		using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(widget.Data) ? "{}" : widget.Data);

		if (!StreamChatWidgetSettings.AllowsModeration(document.RootElement))
		{
			return StreamChatWidgetPermission.TurnedOff;
		}

		var configured = StreamChatWidgetSettings.Account(document.RootElement);
		var resolved = configured ?? _feed.Snapshot(null).Account?.AccountId;

		return string.Equals(resolved, accountId, StringComparison.Ordinal)
			? StreamChatWidgetPermission.Allowed
			: StreamChatWidgetPermission.AccountChanged;
	}

	private IStreamChatModerator? Moderator()
		=> _integrations.Integrations.FirstOrDefault(integration =>
			integration is IStreamChatModerator &&
			string.Equals(integration.Id, _platform.OwnerId, StringComparison.Ordinal) &&
			_integrations.IsEnabled(integration.Id)) as IStreamChatModerator;

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
			OwnerId = _platform.OwnerId,
			Name = "header-icon",
			MediaType = provider.IconMimeType,
			Content = provider.GetIcon(),
		});
	}

	private static string? ReadAccount(UiSurface surface)
		=> surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.Data, out var data)
			? StreamChatWidgetSettings.Account(data)
			: null;

	private static string? ReadString(UiSurface surface, string name)
		=> surface.Attributes.TryGetValue(name, out var element) && element.ValueKind == JsonValueKind.String
			? element.GetString()
			: null;
}
