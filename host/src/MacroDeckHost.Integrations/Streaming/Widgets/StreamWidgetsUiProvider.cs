using System.Text.Json;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Variables;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Widgets;
using MacroDeckHost.Integrations.Ui;
using MacroDeckHost.Localization;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Integrations.Streaming.Widgets;

internal sealed class StreamWidgetsUiProvider : IUiProvider
{
	public const string IconFolder = "widget";

	public const string LogoIcon = "logo";

	internal const string DialogWidgetKey = "widgetId";

	internal const string DialogCultureKey = "culture";

	private readonly StreamPlatform _platform;
	private readonly IStreamChatFeed _feed;
	private readonly IStreamChatImages? _images;
	private readonly IStreamStatsSource _stats;
	private readonly IStreamThumbnails _thumbnails;
	private readonly Func<IIntegrationContext?> _context;
	private readonly Func<IStreamChatModerator?> _moderator;
	private readonly TimeProvider _time;
	private readonly ILogger _logger;

	public StreamWidgetsUiProvider(
		StreamPlatform platform,
		IStreamChatFeed feed,
		IStreamChatImages? images,
		IStreamStatsSource stats,
		IStreamThumbnails thumbnails,
		Func<IIntegrationContext?> context,
		Func<IStreamChatModerator?> moderator,
		TimeProvider time,
		ILogger logger)
	{
		ArgumentNullException.ThrowIfNull(platform);

		_platform = platform;
		_feed = feed;
		_images = images;
		_stats = stats;
		_thumbnails = thumbnails;
		_context = context;
		_moderator = moderator;
		_time = time;
		_logger = logger.ForContext<StreamWidgetsUiProvider>();
	}

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
	[
		new() { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
		new() { Kind = UiSurfaceKinds.Preview, SessionMode = UiSessionModes.Shared },
		new() { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
		new() { Kind = UiSurfaceKinds.Dialog, SessionMode = UiSessionModes.Exclusive }
	];

	public async Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		var surface = request.Surface;

		if (UiWidgetSurface.IsConfigurationFor(surface, _platform.ChatWidgetTypeId))
		{
			return new UiViewSession(new UiView(surface,
				StreamChatWidgetConfigView.Build(_platform, UiWidgetSurface.Data(surface), _feed.Accounts)));
		}

		if (UiWidgetSurface.IsConfigurationFor(surface, _platform.StatsWidgetTypeId))
		{
			return new UiViewSession(new UiView(surface,
				StreamStatsWidgetConfigView.Build(_platform, UiWidgetSurface.Data(surface), _stats.Accounts)));
		}

		if (surface.Kind == UiSurfaceKinds.Dialog)
		{
			return ReadString(surface, UiDialogSurfaceAttributes.ViewId) == _platform.DialogViewId
				? CreateDialog(surface)
				: null;
		}

		if (UiWidgetSurface.IsFor(surface, _platform.StatsWidgetTypeId))
		{
			return await CreateStatsAsync(surface, cancellationToken).ConfigureAwait(false);
		}

		return UiWidgetSurface.IsFor(surface, _platform.ChatWidgetTypeId)
			? await CreateChatAsync(surface, cancellationToken).ConfigureAwait(false)
			: null;
	}

	internal async Task<UiResource?> LogoAsync(CancellationToken cancellationToken)
	{
		if (_context()?.UiResources is not { } resources)
		{
			return null;
		}

		try
		{
			return await resources.GetPluginIconAsync(IconFolder, LogoIcon, cancellationToken).ConfigureAwait(false);
		}
		catch (UiResourceException)
		{
			return null;
		}
	}

	private async Task<IUiSession> CreateChatAsync(UiSurface surface, CancellationToken cancellationToken)
	{
		var culture = UiWidgetSurface.Culture(surface);
		var separator = WidgetText.Resolve(AppStrings.Integrations.StreamChat.Widget.NameSeparator(), culture);
		var data = UiWidgetSurface.Data(surface);
		var cornerRadius = UiWidgetSafeArea.RadiusOf(surface);
		var backgroundColor = StreamChatWidgetSettings.BackgroundColor(data);
		var scale = StreamChatWidgetSettings.FontScale(data);
		var messageColor = StreamChatWidgetSettings.MessageColor(data);
		var layout = StreamChatLines.WidgetLayout(scale, messageColor, StreamChatWidgetSettings.NameColor(data));
		var logo = await LogoAsync(cancellationToken).ConfigureAwait(false);

		if (UiWidgetSurface.IsSampleRequested(surface))
		{
			return new UiViewSession(new UiView(surface,
				StreamChatWidgetView.Build(_platform,
					new UiState<StreamChatViewState>(StreamChatWidgetSample.Build(culture, separator, layout)),
					cornerRadius,
					logo is null ? null : new UiState<UiResource?>(logo),
					backgroundColor: backgroundColor,
					scale: scale,
					messageColor: messageColor)));
		}

		var icon = new StreamWidgetIcon(logo, LogoAsync);
		var accountId = StreamChatWidgetSettings.Account(data);
		var lines = new StreamChatLines(separator, _images, layout);
		var snapshot = _feed.Snapshot(accountId);
		var state = new UiState<StreamChatViewState>(lines.Build(snapshot));
		var widgetId = surface.Kind == UiSurfaceKinds.Widget ? UiWidgetSurface.WidgetId(surface) : null;

		StreamChatWidgetSession? session = null;
		IReadOnlyList<UiEventHandler> events = widgetId is { } id
			? [UiEventHandler.On(UiComponentEvents.Press, () => OpenDialog(session, id, culture))]
			: [];

		var view = new UiView(surface,
			StreamChatWidgetView.Build(_platform,
				state,
				cornerRadius,
				icon.State,
				events,
				backgroundColor,
				scale,
				messageColor));
		session = new StreamChatWidgetSession(view, state, lines, _feed, accountId, snapshot.Account?.AccountId, icon);

		return session;
	}

	private async Task<IUiSession> CreateStatsAsync(UiSurface surface, CancellationToken cancellationToken)
	{
		var data = UiWidgetSurface.Data(surface);
		var cornerRadius = UiWidgetSafeArea.RadiusOf(surface);
		var backgroundColor = StreamStatsWidgetSettings.BackgroundColor(data);
		var options = StreamStatsWidgetSettings.Options(_platform, data);
		var logo = await LogoAsync(cancellationToken).ConfigureAwait(false);

		if (UiWidgetSurface.IsSampleRequested(surface))
		{
			var sample = StreamStatsWidgetSample.Build(_platform, UiWidgetSurface.Culture(surface));

			return new UiViewSession(new UiView(surface,
				StreamStatsWidgetView.Build(_platform,
					new UiState<StreamStatsViewState>(sample),
					options,
					cornerRadius,
					logo is null ? null : new UiState<UiResource?>(logo),
					backgroundColor)));
		}

		var icon = new StreamWidgetIcon(logo, LogoAsync);
		var state = new UiState<StreamStatsViewState>(StreamStatsViewState.Empty);
		var view = new UiView(surface,
			StreamStatsWidgetView.Build(_platform, state, options, cornerRadius, icon.State, backgroundColor));

		return new StreamStatsWidgetSession(_platform,
			view,
			state,
			StreamStatsWidgetSettings.Account(data),
			options,
			_stats,
			OpenHistoryAsync,
			_thumbnails,
			_time,
			icon);
	}

	private async Task<IVariableHistoryWindow?> OpenHistoryAsync(
		string variableName,
		int capacity,
		CancellationToken cancellationToken)
	{
		return _context()?.Variables is { } variables
			? await variables.OpenHistoryAsync(variableName, capacity, cancellationToken).ConfigureAwait(false)
			: null;
	}

	private StreamChatDialogSession CreateDialog(UiSurface surface)
	{
		var data = surface.Attributes.TryGetValue(UiDialogSurfaceAttributes.Data, out var element)
			? element
			: default;
		var accountId = StreamWidgetJson.ReadString(data, StreamChatWidgetType.AccountKey) is { Length: > 0 } account
			? account
			: null;
		var widgetId = Guid.TryParse(StreamWidgetJson.ReadString(data, DialogWidgetKey), out var parsed)
			? parsed
			: (Guid?)null;
		var separator = WidgetText.Resolve(AppStrings.Integrations.StreamChat.Widget.NameSeparator(),
			StreamWidgetJson.ReadString(data, DialogCultureKey));

		return new StreamChatDialogSession(_platform,
			surface,
			new StreamChatLines(separator, _images, StreamChatDialogView.Layout),
			_feed,
			accountId,
			cancellationToken => PermissionAsync(widgetId, accountId, cancellationToken),
			_moderator,
			_logger);
	}

	private void OpenDialog(StreamChatWidgetSession? session, Guid widgetId, string? culture)
	{
		if (session?.DialogRequest() is not { } request)
		{
			return;
		}

		var data = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		{
			[DialogWidgetKey] = JsonSerializer.SerializeToElement(widgetId.ToString())
		};

		if (request.AccountId is { Length: > 0 } accountId)
		{
			data[StreamChatWidgetType.AccountKey] = JsonSerializer.SerializeToElement(accountId);
		}

		if (culture is not null)
		{
			data[DialogCultureKey] = JsonSerializer.SerializeToElement(culture);
		}

		var modal = new ModalDefinition
		{
			ViewId = _platform.DialogViewId,
			Title = request.AccountLabel is { } label ? _platform.Chat.Title(label) : _platform.Chat.Name(),
			Data = data
		};

		_ = ShowDialogAsync(request.Origin, modal);
	}

	private async Task ShowDialogAsync(IUiEventOrigin origin, ModalDefinition modal)
	{
		try
		{
			if (!await origin.ShowModalAsync(modal).ConfigureAwait(false))
			{
				_logger.Warning("The {Platform} chat dialog could not be opened on client {ClientId}",
					_platform.OwnerId,
					origin.ClientId);
			}
		}
#pragma warning disable CA1031 // A press has nobody to report to; the fault is logged instead of lost.
		catch (Exception exception)
#pragma warning restore CA1031
		{
			_logger.Warning(exception,
				"Failed to open the {Platform} chat dialog on client {ClientId}",
				_platform.OwnerId,
				origin.ClientId);
		}
	}

	// Re-read from the stored widget on every check: the dialog outlives the press that opened it, and the
	// widget's settings may have changed since.
	private async Task<StreamChatWidgetPermission> PermissionAsync(
		Guid? widgetId,
		string? accountId,
		CancellationToken cancellationToken)
	{
		if (widgetId is not { } id || accountId is null || _context()?.Widgets is not { } widgets)
		{
			return StreamChatWidgetPermission.TurnedOff;
		}

		var widget = await widgets.GetWidgetDataAsync(id.ToString(), cancellationToken).ConfigureAwait(false);

		if (widget is null
			|| !string.Equals(widget.WidgetTypeId, _platform.ChatWidgetTypeId, StringComparison.Ordinal)
			|| !StreamChatWidgetSettings.AllowsModeration(widget.Data))
		{
			return StreamChatWidgetPermission.TurnedOff;
		}

		var configured = StreamChatWidgetSettings.Account(widget.Data);
		var resolved = configured ?? _feed.Snapshot(null).Account?.AccountId;

		return string.Equals(resolved, accountId, StringComparison.Ordinal)
			? StreamChatWidgetPermission.Allowed
			: StreamChatWidgetPermission.AccountChanged;
	}

	private static string? ReadString(UiSurface surface, string name)
	{
		return surface.Attributes.TryGetValue(name, out var element) && element.ValueKind == JsonValueKind.String
			? element.GetString()
			: null;
	}
}
