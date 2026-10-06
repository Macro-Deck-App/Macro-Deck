using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Applications;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Widgets.Configuration;
using MacroDeckHost.Widgets.Preview;
using Microsoft.Extensions.DependencyInjection;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Widgets.Calendar;

public sealed class CalendarUiProvider : IBuiltInIntegrationUiProvider
{
	public const string DialogViewId = CalendarEventDialogs.ViewId;

	public const string AgendaDialogViewId = CalendarEventDialogs.AgendaViewId;

	public const string AgendaDialogWidgetKey = CalendarEventDialogs.WidgetKey;

	public const string DialogCalendarKey = CalendarEventDialogs.CalendarKey;

	public const string DialogEventKey = CalendarEventDialogs.EventKey;

	private readonly ICalendarEventCache _cache;
	private readonly IFolderCache _folders;
	private readonly CalendarWidgetChanges _widgetChanges;
	private readonly IExternalUrlOpener _opener;
	private readonly TimeProvider _time;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IWidgetSampleTextResolver _text;
	private readonly ILogger _logger;

	public CalendarUiProvider(
		ICalendarEventCache cache,
		IFolderCache folders,
		CalendarWidgetChanges widgetChanges,
		IExternalUrlOpener opener,
		TimeProvider time,
		IServiceScopeFactory scopeFactory,
		IWidgetSampleTextResolver text,
		ILogger logger)
	{
		_cache = cache;
		_folders = folders;
		_widgetChanges = widgetChanges;
		_opener = opener;
		_time = time;
		_scopeFactory = scopeFactory;
		_text = text;
		_logger = logger.ForContext<CalendarUiProvider>();
	}

	public string IntegrationId => CalendarWidgetTypes.OwnerId;

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

		if (surface.Kind == UiSurfaceKinds.Config)
		{
			return CreateConfig(surface);
		}

		if (surface.Kind == UiSurfaceKinds.Dialog)
		{
			return ReadString(surface, UiDialogSurfaceAttributes.ViewId) switch
			{
				DialogViewId => await CreateDialogAsync(surface).ConfigureAwait(false),
				AgendaDialogViewId => await CreateAgendaDialogAsync(surface).ConfigureAwait(false),
				_ => null,
			};
		}

		if (surface.Kind is not (UiSurfaceKinds.Widget or UiSurfaceKinds.Preview))
		{
			return null;
		}

		if (ReadString(surface, UiWidgetSurfaceAttributes.WidgetType) != CalendarWidgetTypes.QualifiedId)
		{
			return null;
		}

		var data = surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.Data, out var element) &&
			element.ValueKind == JsonValueKind.Object
				? element
				: default;

		return CalendarWidgetData.IsNextEvent(data)
			? await CreateWidgetAsync(surface,
					data,
					CalendarNextEventSettings.Parse,
					CalendarNextEvent.Compute,
					CalendarNextEventView.Build)
				.ConfigureAwait(false)
			: await CreateWidgetAsync(surface,
					data,
					CalendarAgendaSettings.Parse,
					CalendarAgenda.Compute,
					CalendarAgendaView.Build)
				.ConfigureAwait(false);
	}

	private WidgetConfigSession? CreateConfig(UiSurface surface)
		=> WidgetConfigSurfaces.IsFor(surface, CalendarWidgetTypes.QualifiedId)
			? new WidgetConfigSession(new UiView(surface,
				CalendarWidgetConfigViews.Build(WidgetConfigSurfaces.Data(surface), _cache)))
			: null;

	private async Task<IUiSession> CreateWidgetAsync<TSettings, TState>(
		UiSurface surface,
		JsonElement data,
		Func<JsonElement, TSettings> parse,
		Func<CalendarSnapshot, DateTimeOffset, TSettings, CalendarFormat, CalendarComputed<TState>> compute,
		Func<UiState<TState>, TSettings, int, string?, MacroDeck.Ui.Dsl.UiElement> build)
	{
		var settings = parse(data);
		var format = await FormatAsync().ConfigureAwait(false);
		var radius = WidgetSafeArea.RadiusOf(surface);
		var background = CalendarWidgetSettings.BackgroundColor(data);

		if (WidgetSamplePreview.IsRequested(surface))
		{
			var now = _time.GetUtcNow();
			var sample = await CalendarWidgetSample.BuildAsync(_text, now, format.TimeZone).ConfigureAwait(false);
			var state = new UiState<TState>(compute(sample, now, settings, format).State);

			return new StaticWidgetUiSession(new UiView(surface, build(state, settings, radius, background)));
		}

		return new CalendarWidgetSession<TState>(surface,
			_cache,
			_time,
			format,
			TryFormatAsync,
			(snapshot, now, current) => compute(snapshot, now, settings, current),
			state => build(state, settings, radius, background));
	}

	private async Task<IUiSession> CreateDialogAsync(UiSurface surface)
	{
		var data = surface.Attributes.TryGetValue(UiDialogSurfaceAttributes.Data, out var element)
			? element
			: default;

		return new CalendarEventDialogSession(surface,
			_cache,
			_opener,
			await FormatAsync().ConfigureAwait(false),
			WidgetConfigJson.ReadString(data, DialogCalendarKey) ?? string.Empty,
			WidgetConfigJson.ReadString(data, DialogEventKey) ?? string.Empty,
			_logger);
	}

	private async Task<IUiSession> CreateAgendaDialogAsync(UiSurface surface)
	{
		var data = surface.Attributes.TryGetValue(UiDialogSurfaceAttributes.Data, out var element)
			? element
			: default;

		return new CalendarAgendaDialogSession(surface,
			_cache,
			_folders,
			_widgetChanges,
			_opener,
			_time,
			await FormatAsync().ConfigureAwait(false),
			WidgetConfigJson.ReadString(data, AgendaDialogWidgetKey) ?? string.Empty,
			_logger);
	}

	private async Task<CalendarFormat> FormatAsync()
		=> new(await TimeFormatResolver.ResolveAsync(_scopeFactory).ConfigureAwait(false), _cache.TimeZone);

	private async Task<CalendarFormat?> TryFormatAsync()
	{
		try
		{
			return await FormatAsync().ConfigureAwait(false);
		}
#pragma warning disable CA1031 // A timer refresh has nobody to report to; the widget keeps its last format.
		catch (Exception exception)
#pragma warning restore CA1031
		{
			_logger.Warning(exception, "Reading the time format for a calendar widget failed");
			return null;
		}
	}

	private static string? ReadString(UiSurface surface, string name)
		=> surface.Attributes.TryGetValue(name, out var element) && element.ValueKind == JsonValueKind.String
			? element.GetString()
			: null;
}
